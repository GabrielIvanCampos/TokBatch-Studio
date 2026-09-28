package main

import (
	"bufio"
	"embed"
	"encoding/json"
	"fmt"
	"io"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"
	"syscall"
	"unsafe"

	"github.com/jchv/go-webview2"
)

//go:embed ui/*
var uiFS embed.FS

type studio struct {
	w    webview2.WebView
	mu   sync.Mutex
	cmd  *exec.Cmd
	done chan struct{}
}

type downloadReq struct {
	URL     string `json:"url"`
	Dest    string `json:"dest"`
	Cookies string `json:"cookies"`
	Skip    bool   `json:"skip"`
	JSON    bool   `json:"json"`
	Thumb   bool   `json:"thumb"`
	Update  bool   `json:"update"`
}

func main() {
	index, err := materializeUI()
	if err != nil {
		fail(err.Error())
		return
	}
	w := webview2.NewWithOptions(webview2.WebViewOptions{
		Debug:     false,
		AutoFocus: true,
		WindowOptions: webview2.WindowOptions{
			Title:  "TokBatch Studio",
			Width:  1280,
			Height: 720,
			Center: true,
		},
	})
	if w == nil {
		fail("O WebView2 não está disponível neste Windows.")
		return
	}
	defer w.Destroy()

	app := &studio{w: w}
	home, _ := os.UserHomeDir()
	dest := filepath.Join(home, "Downloads", "TokBatch")
	_ = os.MkdirAll(dest, 0o755)
	destJSON, _ := json.Marshal(dest)
	w.Init(`document.addEventListener('DOMContentLoaded', function () {
		var d = document.getElementById('dest');
		if (d) d.value = ` + string(destJSON) + `;
		var c = document.getElementById('cfg-dest');
		if (c && d) c.textContent = d.value;
	});`)

	_ = w.Bind("pasteClipboard", clipboardText)
	_ = w.Bind("pickFolder", pickFolder)
	_ = w.Bind("openFolder", openFolder)
	_ = w.Bind("appInfo", func() string { return appInfo() })
	_ = w.Bind("startDownload", app.startDownload)
	_ = w.Bind("stopDownload", app.stopDownload)

	w.Navigate(fileURL(index))
	w.Run()
}

func (a *studio) startDownload(raw string) string {
	var req downloadReq
	if err := json.Unmarshal([]byte(raw), &req); err != nil {
		return err.Error()
	}
	req.URL = strings.TrimSpace(req.URL)
	req.Dest = strings.TrimSpace(req.Dest)
	if req.URL == "" || req.Dest == "" {
		return "Informe o link e a pasta."
	}
	tool := ytPath()
	if tool == "" {
		a.finish(false, "yt-dlp.exe não foi encontrado. Coloque o executável ao lado do programa ou em %LOCALAPPDATA%\\TokBatch Studio\\bin.")
		return "missing"
	}
	a.mu.Lock()
	if a.cmd != nil {
		a.mu.Unlock()
		return "busy"
	}
	done := make(chan struct{})
	a.done = done
	a.mu.Unlock()

	go a.run(tool, req, done)
	return "ok"
}

func (a *studio) run(tool string, req downloadReq, done chan struct{}) {
	ok := false
	msg := "O download não foi concluído."
	defer func() {
		a.mu.Lock()
		a.cmd = nil
		a.mu.Unlock()
		close(done)
		a.finish(ok, msg)
	}()
	if err := os.MkdirAll(req.Dest, 0o755); err != nil {
		msg = err.Error()
		return
	}
	if req.Update {
		a.log("Atualizando yt-dlp…")
		up := exec.Command(tool, "-U")
		hide(up)
		out, err := up.CombinedOutput()
		if text := strings.TrimSpace(string(out)); text != "" {
			a.log(text)
		}
		if err != nil {
			a.log("Não foi possível atualizar o yt-dlp. O download continua.")
		}
	}
	args := []string{
		"--newline",
		"--no-mtime",
		"--windows-filenames",
		"-o", filepath.Join(req.Dest, "%(uploader)s", "%(id)s.%(ext)s"),
		req.URL,
	}
	if req.Skip {
		args = append([]string{"--no-overwrites"}, args...)
	}
	if req.JSON {
		args = append(args, "--write-info-json")
	}
	if req.Thumb {
		args = append(args, "--write-thumbnail")
	}
	if req.Cookies != "" {
		args = append(args, "--cookies-from-browser", req.Cookies)
	}
	cmd := exec.Command(tool, args...)
	hide(cmd)
	stdout, err := cmd.StdoutPipe()
	if err != nil {
		msg = err.Error()
		return
	}
	stderr, err := cmd.StderrPipe()
	if err != nil {
		msg = err.Error()
		return
	}
	if err := cmd.Start(); err != nil {
		msg = err.Error()
		return
	}
	a.mu.Lock()
	a.cmd = cmd
	a.mu.Unlock()
	a.log("Baixando " + req.URL)
	var wg sync.WaitGroup
	wg.Add(2)
	go func() { defer wg.Done(); a.scan(stdout) }()
	go func() { defer wg.Done(); a.scan(stderr) }()
	wg.Wait()
	if err := cmd.Wait(); err != nil {
		return
	}
	ok = true
	msg = "Concluído."
}

func (a *studio) scan(r io.Reader) {
	sc := bufio.NewScanner(r)
	sc.Buffer(make([]byte, 0, 64*1024), 1024*1024)
	for sc.Scan() {
		line := strings.TrimSpace(sc.Text())
		if line != "" {
			a.log(line)
		}
	}
}

func (a *studio) stopDownload() {
	a.mu.Lock()
	cmd := a.cmd
	a.mu.Unlock()
	if cmd != nil && cmd.Process != nil {
		_ = cmd.Process.Kill()
	}
}

func (a *studio) log(line string) {
	b, _ := json.Marshal(line)
	a.w.Dispatch(func() {
		a.w.Eval("window.appendLog(" + string(b) + ")")
	})
}

func (a *studio) finish(ok bool, message string) {
	b, _ := json.Marshal(message)
	flag := "false"
	if ok {
		flag = "true"
	}
	a.w.Dispatch(func() {
		a.w.Eval("window.onDownloadDone(" + flag + "," + string(b) + ")")
	})
}

func hide(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x08000000}
}

func ytPath() string {
	candidates := []string{
		filepath.Join(filepath.Dir(os.Args[0]), "yt-dlp.exe"),
		filepath.Join(os.Getenv("LOCALAPPDATA"), "TokBatch Studio", "bin", "yt-dlp.exe"),
	}
	for _, p := range candidates {
		if fileExists(p) {
			return p
		}
	}
	if p, err := exec.LookPath("yt-dlp.exe"); err == nil {
		return p
	}
	if p, err := exec.LookPath("yt-dlp"); err == nil {
		return p
	}
	return ""
}

func fileExists(p string) bool {
	st, err := os.Stat(p)
	return err == nil && !st.IsDir()
}

func appInfo() string {
	tool := ytPath()
	if tool == "" {
		tool = "não encontrado"
	}
	b, _ := json.Marshal(map[string]string{"ytdlp": tool})
	return string(b)
}

func materializeUI() (string, error) {
	dir := filepath.Join(os.TempDir(), "TokBatchStudio", "ui")
	if err := os.RemoveAll(dir); err != nil {
		return "", err
	}
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return "", err
	}
	entries, err := fs.ReadDir(uiFS, "ui")
	if err != nil {
		return "", err
	}
	for _, e := range entries {
		if e.IsDir() {
			continue
		}
		b, err := fs.ReadFile(uiFS, "ui/"+e.Name())
		if err != nil {
			return "", err
		}
		if err := os.WriteFile(filepath.Join(dir, e.Name()), b, 0o644); err != nil {
			return "", err
		}
	}
	return filepath.Join(dir, "index.html"), nil
}

func fileURL(path string) string {
	p := filepath.ToSlash(path)
	if !strings.HasPrefix(p, "/") {
		p = "/" + p
	}
	return "file://" + p
}

func pickFolder(current string) string {
	script := fmt.Sprintf(`
Add-Type -AssemblyName System.Windows.Forms
$d = New-Object System.Windows.Forms.FolderBrowserDialog
$d.Description = 'Escolher pasta de destino'
$d.SelectedPath = '%s'
if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.SelectedPath) }
`, strings.ReplaceAll(current, "'", "''"))
	cmd := exec.Command("powershell", "-NoProfile", "-STA", "-Command", script)
	hide(cmd)
	out, err := cmd.Output()
	if err != nil {
		return ""
	}
	return strings.TrimSpace(string(out))
}

func openFolder(path string) bool {
	if strings.TrimSpace(path) == "" {
		return false
	}
	if err := os.MkdirAll(path, 0o755); err != nil {
		return false
	}
	cmd := exec.Command("explorer.exe", path)
	return cmd.Start() == nil
}

var (
	user32           = syscall.NewLazyDLL("user32.dll")
	kernel32         = syscall.NewLazyDLL("kernel32.dll")
	procOpenClip     = user32.NewProc("OpenClipboard")
	procCloseClip    = user32.NewProc("CloseClipboard")
	procGetClip      = user32.NewProc("GetClipboardData")
	procGlobalLock   = kernel32.NewProc("GlobalLock")
	procGlobalUnlock = kernel32.NewProc("GlobalUnlock")
	procMsgBox       = user32.NewProc("MessageBoxW")
)

func clipboardText() string {
	r, _, _ := procOpenClip.Call(0)
	if r == 0 {
		return ""
	}
	defer procCloseClip.Call()
	h, _, _ := procGetClip.Call(13)
	if h == 0 {
		return ""
	}
	p, _, _ := procGlobalLock.Call(h)
	if p == 0 {
		return ""
	}
	defer procGlobalUnlock.Call(h)
	return syscall.UTF16ToString((*[1 << 20]uint16)(unsafe.Pointer(p))[:1<<20])
}

func fail(msg string) {
	ptr, _ := syscall.UTF16PtrFromString(msg)
	title, _ := syscall.UTF16PtrFromString("TokBatch Studio")
	procMsgBox.Call(0, uintptr(unsafe.Pointer(ptr)), uintptr(unsafe.Pointer(title)), 0x10)
}

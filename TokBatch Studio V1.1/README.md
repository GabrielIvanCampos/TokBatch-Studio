# TokBatch Studio V1.1

Este pacote contém o executável fornecido nesta conversa e um projeto-fonte reconstruído.

## Importante
O código-fonte original usado para compilar o binário V1.1 não estava mais disponível no ambiente quando este pacote foi montado. Por isso, `src/main.go` é uma **reconstrução limpa e buildável**, e não uma cópia byte-a-byte do código original.

## Arquivos
- `TokBatch_Studio_V1_1.exe` — binário V1.1 fornecido.
- `src/main.go` — reconstrução do código Go/Win32.
- `go.mod` — módulo Go.
- `build/build.bat` — script de compilação para Windows.

## Requisitos para recompilar
- Windows 10/11
- Go 1.23+ (ou versão compatível)
- yt-dlp.exe não é empacotado neste ZIP.

## Compilar
Execute `build\build.bat` em um Prompt de Comando do Windows com Go instalado.

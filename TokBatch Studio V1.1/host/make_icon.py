from PIL import Image

src = r"C:\Users\Administrador\.cursor\projects\c-Projetos-TokBatch-Studio\assets\c__Users_Administrador_AppData_Roaming_Cursor_User_workspaceStorage_7d1b94d744a1c18924b5f4d71200a5ae_images_image-c2150271-bc95-454a-9a65-6894f2fae554.jpg"
im = Image.open(src).convert("RGBA")
px = im.load()
w, h = im.size
seen = set()
stack = [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]
while stack:
    x, y = stack.pop()
    if x < 0 or y < 0 or x >= w or y >= h or (x, y) in seen:
        continue
    r, g, b, a = px[x, y]
    if r > 18 or g > 18 or b > 18:
        continue
    seen.add((x, y))
    px[x, y] = (0, 0, 0, 0)
    stack.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))

ui = r"C:\Projetos\TokBatch_Studio\src\ui"
im.resize((256, 256), Image.Resampling.LANCZOS).save(ui + r"\icon.png")
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
frames = [im.resize(s, Image.Resampling.LANCZOS) for s in sizes]
frames[-1].save(ui + r"\favicon.ico", format="ICO", sizes=sizes, append_images=frames[:-1])
frames[-1].save(r"C:\Projetos\TokBatch_Studio\host\app.ico", format="ICO", sizes=sizes, append_images=frames[:-1])
print("icon ok", len(seen))

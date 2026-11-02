# Printable page for the marker: exactly 15 cm wide at 300 dpi, on A4 and on US Letter, with the seats marked.
import sys
from PIL import Image, ImageDraw, ImageFont
marker_path, pdf_path, png_path = sys.argv[1:4]
DPI = 300
MM = DPI / 25.4
SIZE_MM = 150
F = '/System/Library/Fonts/Supplemental/Arial.ttf'
FB = '/System/Library/Fonts/Supplemental/Arial Bold.ttf'
marker = Image.open(marker_path).convert('RGB').resize((round(SIZE_MM * MM), round(SIZE_MM * MM)), Image.LANCZOS)

def page(w_mm, h_mm, name):
    W, H = round(w_mm * MM), round(h_mm * MM)
    im = Image.new('RGB', (W, H), 'white')
    d = ImageDraw.Draw(im)
    big = ImageFont.truetype(FB, round(6 * MM))
    mid = ImageFont.truetype(FB, round(4.2 * MM))
    small = ImageFont.truetype(F, round(3.2 * MM))
    mx = (W - marker.width) // 2
    my = (H - marker.height) // 2 + round(4 * MM)
    im.paste(marker, (mx, my))
    def center(text, y, font, fill='black'):
        tw = d.textlength(text, font=font)
        d.text(((W - tw) / 2, y), text, font=font, fill=fill)
    center('Card Wars · Versus en la mesa', round(10 * MM), big)
    center('Imprime al 100 % (tamaño real, sin «ajustar a la página»). El cuadro debe medir 15 cm de lado.', round(19 * MM), small)
    # Seats
    top_y = my - round(17 * MM)
    center('▲  Aquí se sienta quien SE UNE (invitado)  ▲', top_y, mid, (30, 70, 160))
    bot_y = my + marker.height + round(5 * MM)
    center('▼  Aquí se sienta quien CREA la partida (anfitrión)  ▼', bot_y, mid, (170, 40, 30))
    center('El borde con la muesca blanca apunta al anfitrión. Pon la hoja plana en el centro de la mesa.', bot_y + round(8 * MM), small)
    # 5 cm check ruler
    rx = round(20 * MM)
    ry = H - round(18 * MM)
    d.line([(rx, ry), (rx + round(50 * MM), ry)], fill='black', width=4)
    for i in range(6):
        x = rx + round(i * 10 * MM)
        d.line([(x, ry - round(3 * MM)), (x, ry)], fill='black', width=4)
    d.text((rx, ry + round(1.5 * MM)), 'Regla de control: 5 cm', font=small, fill='black')
    d.text((W - round(80 * MM), ry + round(1.5 * MM)), 'Papel: ' + name, font=small, fill=(90, 90, 90))
    return im

a4 = page(210, 297, 'A4')
letter = page(215.9, 279.4, 'Carta')
a4.save(pdf_path, 'PDF', resolution=DPI, save_all=True, append_images=[letter])
a4.save(png_path, dpi=(DPI, DPI))
print('ok', a4.size, letter.size)

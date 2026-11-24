using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

// Генерирует app.ico (16…256 px, PNG внутри) из src\Logo.cs. Запуск: MakeIcon.exe app.ico [preview.png]
static class MakeIcon
{
    static void Main(string[] args)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int size in sizes) pngs.Add(Render(size));

        using (FileStream fs = File.Create(args[0]))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] p in pngs) w.Write(p);
        }

        if (args.Length > 1)
        {
            // Превью: все размеры в ряд на тёмном и светлом фоне
            using (Bitmap bmp = new Bitmap(760, 560))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(13, 14, 17));
                using (SolidBrush light = new SolidBrush(Color.FromArgb(236, 238, 241))) g.FillRectangle(light, 0, 280, 760, 280);
                for (int band = 0; band < 2; band++)
                {
                    int x = 20;
                    foreach (int size in new int[] { 256, 64, 48, 32, 24, 16 })
                    {
                        using (Bitmap one = new Bitmap(new MemoryStream(Render(size))))
                            g.DrawImage(one, x, band * 280 + 140 - size / 2, size, size);
                        x += size + 30;
                    }
                }
                bmp.Save(args[1], ImageFormat.Png);
            }
        }
    }

    static byte[] Render(int size)
    {
        using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                float pad = size >= 32 ? size * 0.03f : 0;
                Tunex.Logo.Draw(g, new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad));
            }
            using (MemoryStream ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }
    }
}

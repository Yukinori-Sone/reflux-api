using System;
using System.Drawing;
using System.Drawing.Imaging;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Resource = SharpDX.DXGI.Resource;

namespace Reflux.ServerMonitor
{
    /// <summary>
    /// Frames of one screen through the DXGI Desktop Duplication API, which also sees exclusive fullscreen games (a GDI screen
    /// copy gets only black then). Kept in its own class so SharpDX (embedded in the exe) is loaded only when a screenshot is taken.
    /// One instance is kept open for a burst of frames.
    /// </summary>
    sealed class DesktopDuplication : IDisposable
    {
        readonly string deviceName;
        Factory1 factory;
        Adapter1 adapter;
        Output1 output;
        Device device;
        OutputDuplication dup;

        /// <summary>The screen with that device name (\\.\DISPLAY1)</summary>
        public DesktopDuplication(string deviceName)
        {
            this.deviceName = deviceName;
            Open();
        }

        void Open()
        {
            Close();
            factory = new Factory1();
            for (int a = 0; a < factory.GetAdapterCount1(); a++)
            {
                var ad = factory.GetAdapter1(a);
                for (int o = 0; o < ad.GetOutputCount(); o++)
                {
                    using var op = ad.GetOutput(o);
                    if (!string.Equals(op.Description.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) { continue; }
                    adapter = ad;
                    output = op.QueryInterface<Output1>();
                    device = new Device(adapter);
                    dup = output.DuplicateOutput(device);
                    return;
                }
                ad.Dispose();
            }
            throw new InvalidOperationException("no output " + deviceName);
        }

        void Close()
        {
            dup?.Dispose(); dup = null;
            device?.Dispose(); device = null;
            output?.Dispose(); output = null;
            adapter?.Dispose(); adapter = null;
            factory?.Dispose(); factory = null;
        }

        public void Dispose() => Close();

        /// <summary>
        /// The next frame the game presented (waits up to about 3 s), or null with the reason. Reopens the duplication once when
        /// it was lost (display mode change, a secure desktop such as UAC).
        /// </summary>
        public Bitmap Next(out string reason)
        {
            for (int tries = 0; tries < 6; tries++)
            {
                var r = dup.TryAcquireNextFrame(500, out var info, out Resource resource);
                if (r.Failure)
                {
                    if (r.Code == SharpDX.DXGI.ResultCode.AccessLost.Result.Code) { Open(); }
                    continue;
                }
                try
                {
                    if (info.LastPresentTime == 0) { continue; } /* Only a mouse update */
                    using var tex = resource.QueryInterface<Texture2D>();
                    var desc = tex.Description;
                    if (desc.Format != Format.B8G8R8A8_UNorm) { reason = "unsupported format " + desc.Format; return null; }
                    using var staging = new Texture2D(device, new Texture2DDescription
                    {
                        Width = desc.Width,
                        Height = desc.Height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = desc.Format,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CpuAccessFlags = CpuAccessFlags.Read,
                        OptionFlags = ResourceOptionFlags.None,
                    });
                    var context = device.ImmediateContext;
                    context.CopyResource(tex, staging); /* SharpDX order: source, destination */
                    var box = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
                    try
                    {
                        var bmp = new Bitmap(desc.Width, desc.Height, PixelFormat.Format24bppRgb);
                        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                        try { CopyBgraToBgr(box.DataPointer, box.RowPitch, bd.Scan0, bd.Stride, bmp.Width, bmp.Height); }
                        finally { bmp.UnlockBits(bd); }
                        reason = null;
                        return bmp;
                    }
                    finally { context.UnmapSubresource(staging, 0); }
                }
                finally
                {
                    resource?.Dispose();
                    dup.ReleaseFrame();
                }
            }
            reason = "no frame";
            return null;
        }

        static unsafe void CopyBgraToBgr(IntPtr src, int srcPitch, IntPtr dst, int dstStride, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                byte* s = (byte*)src + (long)y * srcPitch;
                byte* d = (byte*)dst + (long)y * dstStride;
                for (int x = 0; x < width; x++, s += 4, d += 3) { d[0] = s[0]; d[1] = s[1]; d[2] = s[2]; }
            }
        }
    }
}

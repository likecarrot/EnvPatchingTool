using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PeStubGen
{
    public static class App
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // 离屏截图：PeStubGen.exe --shot <png> [exe] [宽] [高]
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--shot", StringComparison.OrdinalIgnoreCase))
            {
                string png = args[1];
                string exe = args.Length > 2 ? args[2] : null;
                int w = args.Length > 3 ? int.Parse(args[3]) : 1100;
                int h = args.Length > 4 ? int.Parse(args[4]) : 780;
                return RenderShot(png, exe, w, h);
            }

            // 无头跑一遍完整流程（走界面逻辑，只是不弹窗）：PeStubGen.exe --auto <exe> [输出目录]
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--auto", StringComparison.OrdinalIgnoreCase))
            {
                string exe = args[1];
                string outDir = args.Length > 2 ? args[2] : null;
                return RunAuto(exe, outDir);
            }

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                MessageBox.Show("程序遇到未处理的异常：\r\n\r\n" + e.Exception.Message, "PE 导出桩生成器",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };

            string initial = null;
            if (args != null && args.Length > 0) initial = args[0];

            MainWindow window = new MainWindow(initial);
            app.Run(window);
            return 0;
        }

        private static int RenderShot(string png, string exe, int width, int height)
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            MainWindow window = new MainWindow(null);
            window.Width = width;
            window.Height = height;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = 0;
            window.Top = 0;
            window.Show();
            window.UpdateLayout();

            if (!string.IsNullOrEmpty(exe)) window.LoadForShot(exe);
            window.UpdateLayout();
            DoEvents();

            FrameworkElement root = window.Content as FrameworkElement;
            if (root == null)
            {
                Console.Error.WriteLine("窗口没有内容。");
                return 1;
            }

            int pw = (int)Math.Ceiling(window.ActualWidth);
            int ph = (int)Math.Ceiling(window.ActualHeight);
            if (pw <= 0 || ph <= 0)
            {
                Console.Error.WriteLine("窗口尺寸无效：" + pw + "x" + ph);
                return 1;
            }

            RenderTargetBitmap bmp = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(window);

            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            string dir = Path.GetDirectoryName(Path.GetFullPath(png));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (FileStream fs = File.Create(png))
            {
                enc.Save(fs);
            }

            window.Close();
            app.Shutdown();
            Console.WriteLine("saved " + png + " (" + pw + "x" + ph + ")");
            return 0;
        }

        private static int RunAuto(string exe, string outDir)
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            MainWindow window = new MainWindow(null);
            window.SilentMode = true;
            window.Width = 1100;
            window.Height = 780;
            window.Show();
            window.UpdateLayout();

            window.LoadForShot(exe);
            if (!string.IsNullOrEmpty(outDir)) window.SetOutputDir(outDir);
            window.SetCompile(true);

            window.StartGenerateForShot();

            DateTime deadline = DateTime.Now.AddMinutes(3);
            while (window.IsBusy && DateTime.Now < deadline)
            {
                DoEvents();
                Thread.Sleep(40);
            }
            DoEvents();

            Console.WriteLine("STATUS: " + window.StatusText);
            Console.WriteLine("--- LOG ---");
            Console.WriteLine(window.LogText);
            Console.WriteLine("--- END ---");

            window.Close();
            app.Shutdown();
            return window.IsBusy ? 9 : 0;
        }

        private static void DoEvents()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(delegate(object f)
                {
                    ((DispatcherFrame)f).Continue = false;
                    return null;
                }), frame);
            Dispatcher.PushFrame(frame);
        }
    }
}

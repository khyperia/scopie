using Eto.Forms;
using System;
using System.Threading;

namespace Scopie
{
    public class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var app = new Application();
            var window = new Form()
            {
                Title = "Scopie",
                Content = UserInterface.MakeUi(),
                Width = 1000,
                Height = 600,
            };
            try
            {
                app.Run(window);
            }
            catch (TimeoutException e)
            {
                Console.WriteLine($"Got TimeoutException");
                Console.WriteLine(e);
                Console.WriteLine($"Probably the mount serial port is being weird. (Is the mount on?)");
                Console.ReadKey(true);
            }
        }

        private static int _mainThreadId;

        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;
    }
}

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
            app.Run(window);
        }

        private static int _mainThreadId;

        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;
    }

    public struct MyTuple<T>
    {
        public T Item1
        {
            get;
        }
        public T Item2
        {
            get;
        }

        public MyTuple(T item1, T item2)
        {
            Item1 = item1;
            Item2 = item2;
        }

        public override string ToString()
            => Item1.ToString() + "," + Item2.ToString();
    }

    public static class MyTuple
    {
        public static bool TryParse(string s, out MyTuple<double> result)
        {
            var split = s.Split(',');
            if (split.Length != 2)
            {
                result = default(MyTuple<double>);
                return false;
            }
            if (double.TryParse(split[0], out var item1) && double.TryParse(split[1], out var item2))
            {
                result = new MyTuple<double>(item1, item2);
                return true;
            }
            else
            {
                result = default(MyTuple<double>);
                return false;
            }
        }

        public static string ToString(this MyTuple<double> tup, string format)
            => tup.Item1.ToString(format) + "," + tup.Item2.ToString(format);
    }
}

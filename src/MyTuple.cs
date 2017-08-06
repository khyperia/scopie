namespace Scopie
{
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
        public static bool TryParse(string s, out MyTuple<Dms> result)
        {
            var split = s.Split(',');
            if (split.Length != 2)
            {
                result = default(MyTuple<Dms>);
                return false;
            }
            if (Dms.TryParse(split[0], out var item1) && Dms.TryParse(split[1], out var item2))
            {
                result = new MyTuple<Dms>(item1, item2);
                return true;
            }
            else
            {
                result = default(MyTuple<Dms>);
                return false;
            }
        }

        public static string ToRaDecString(this MyTuple<Dms> dms)
            => dms.Item1.ToDmsString('h') + "," + dms.Item2.ToDmsString('d');
    }
}

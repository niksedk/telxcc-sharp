using System;

namespace TelxCCSharp
{
    class Program
    {
        static int Main(string[] args)
        {
            var returnValue = TelxCC.RunMain(args);
            Console.ReadKey();
            return returnValue;
        }
    }
}

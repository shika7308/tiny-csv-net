using System;
using System.IO;

namespace TinyCsvNet.Sample
{
    class Program
    {
        static void Main(string[] args)
        {
            var csv = @"C:\Users\darus\Downloads\animation_curve_test\Book1.csv";
            using var file = File.OpenRead(csv);
            var aaa = CsvReader.ParseAsListAsync(file, CsvOption.Default);

        }
    }
}

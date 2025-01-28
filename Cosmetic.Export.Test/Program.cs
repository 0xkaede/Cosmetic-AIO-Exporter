using Cosmetic.Exporter.Services;
using System;

namespace Cosmetic.Export.Test
{
    internal class Program
    {

        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            var skunk = new FileProviderService();

            await skunk.Init();

            await skunk.ExportCosmetic("EID_Apollo", Exporter.Enums.ItemType.Emote);
        }
    }
}
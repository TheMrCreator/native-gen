﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

using Newtonsoft.Json;

/*
    Huge shoutout goes to JohnnyCrazy for writing most of the original code
    Without his work, this wouldn't have been possible!
    (https://github.com/JohnnyCrazy/scripthookvdotnet/blob/native-generator/helpers/NativeGenerator/NativeGenerator.cs)
*/
namespace NativeGenerator
{
    class Program
    {
        private static Json.NativeFile DownloadNativesFile(string url)
        {
            using (var wc = new WebClient())
            {
                wc.Headers.Add("Accept-Encoding: gzip, deflate, sdch");

                var data = wc.DownloadData(url);
                var rawData = "{}";

                using (var ms = new MemoryStream())
                using (var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
                {
                    var decompress = new byte[data.Length];
                    var count = 0;

                    while ((count = gz.Read(decompress, 0, decompress.Length)) > 0)
                        ms.Write(decompress, 0, count);

                    rawData = Encoding.UTF8.GetString(ms.ToArray());
                }

                return JsonConvert.DeserializeObject<Json.NativeFile>(rawData);
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Downloading natives.json");

            var nativesFile = DownloadNativesFile("http://www.dev-c.com/nativedb/natives.json");
            var sb = new StringBuilder();

            if (nativesFile == null)
            {
                Console.WriteLine("Failed to download natives.json! Terminating...");
                return;
            }

            var nativeDump = NativeDumpFile.Open(@"C:\Dev\Research\GTA 5\native_lookup.dat");

            var parsedNatives = 0;
            var foundNatives = 0;

            foreach (var nativeNamespace in nativesFile.Keys)
            {
                Console.WriteLine("Processing namespace: {0}", nativeNamespace);

                var natives = nativesFile[nativeNamespace];

                foreach (var nativeHash in natives.Keys)
                {
                    var native = natives[nativeHash];
                    var info = nativeDump[long.Parse(nativeHash.Substring(2), NumberStyles.HexNumber)];

                    if (info != null)
                    {
                        var name = (!String.IsNullOrEmpty(native.Name)) ? native.Name : nativeHash.Substring(2);
                        info.Name = $"{nativeNamespace}__{name}";

                        sb.AppendLine($"{nativeNamespace}::{name} @ 0x{info.FunctionOffset:X} // 0x{info.Hash:X} {native.JHash}");
                        foundNatives++;
                    }

                    parsedNatives++;
                }
            }

            Console.WriteLine($"Finished parsing {parsedNatives} natives. Found {foundNatives} / {nativeDump.Natives.Count} natives that matched the dump file.");
            File.WriteAllText(@"C:\Dev\Research\GTA 5\native_gen.log", sb.ToString());

            Console.WriteLine("Creating script");

            IDAScriptWriterBase scriptWriter = null;
            
            // TODO: make proper argument parser
            if (args.Contains("--py"))
                scriptWriter = new IDAPythonWriter();
            else
                scriptWriter = new IDAScriptWriter();

            scriptWriter.WritePreamble($"This file was automatically generated by NativeGenerator {Assembly.GetExecutingAssembly().GetName().Version}");
            scriptWriter.OpenMainBlock();

            var useLower = args.Contains("--lc");

            foreach (var native in nativeDump.Natives)
            {
                var name = (useLower) ? native.Name.ToLower() : native.Name;

                scriptWriter.WriteMethodCall("MakeName", $"0x{native.FunctionOffset:X}", $"\"{name}\"");
                scriptWriter.WriteComment($"{native.Hash:X}");
                scriptWriter.WriteLine();
            }

            scriptWriter.CloseMainBlock();
            scriptWriter.SaveFile(@"C:\Dev\Research\GTA 5\", "native_gen");

            Console.WriteLine("Operation completed.");
            
            if (System.Diagnostics.Debugger.IsAttached)
                Console.ReadKey();
        }
    }
}

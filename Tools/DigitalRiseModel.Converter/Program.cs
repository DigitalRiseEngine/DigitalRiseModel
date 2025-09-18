﻿using DigitalRise.TextureConverter;
using System;
using System.Reflection;

namespace DigitalRiseModel.Converter
{
	internal static class Program
	{
		public static string Version
		{
			get
			{
				var assembly = typeof(Program).Assembly;
				var name = new AssemblyName(assembly.FullName);

				return name.Version.ToString();
			}
		}

		static void Log(string message) => Console.WriteLine(message);

		static string ParseString(string name, string[] args, ref int i)
		{
			++i;
			if (i >= args.Length)
			{
				throw new ModelConverterException($"Value isn't provided for '{name}'");
			}

			return args[i];
		}

		static bool ParseBool(string name, string[] args, ref int i)
		{
			var value = ParseString(name, args, ref i);

			bool result;
			if (!bool.TryParse(value, out result))
			{
				throw new Exception($"Unable to parse bool value '{args[i]}' for the argument '{name}'.");
			}

			return result;
		}


		static void ShowUsage()
		{
			Console.WriteLine($"drmodconv {Version}");
			Console.WriteLine("Usage: drmodconv <inputFile> <outputFile> [options]");
			Console.WriteLine();
			Console.WriteLine("Options:");

			var grid = new AsciiGrid();

			grid.SetMaximumWidth(0, 30);

			grid.SetValue(0, 0, "-t, --generateTangents");
			grid.SetValue(1, 0, "If enabled, then the tangents and bitangents are generated.");
			grid.SetValue(0, 1, "-f, --flipWindingOrder");
			grid.SetValue(1, 1, "If enabled, then the winding order is flipped.");
			grid.SetValue(0, 2, "-om, --overwriteModel true|false");
			grid.SetValue(1, 2, "If enabled, then the model file(s) are overwritten if exists. The default value is 'true'.");

			Console.WriteLine(grid.ToString());
		}

		static void Process(string[] args)
		{
			if (args.Length == 0 || args.Length == 1 && (args[0] == "-h" || args[0] == "--help"))
			{
				ShowUsage();
				return;
			}

			var options = new Options();

			for (var i = 0; i < args.Length; ++i)
			{
				var arg = args[i];

				switch (arg)
				{
					case "-t":
					case "--generateTangents":
						options.GenerateTangentsAndBitangents = true;
						break;

					case "-f":
					case "--flipWindingOrder":
						options.FlipWindingOrder = true;
						break;

					case "-om":
					case "--overwriteModel":
						options.OverwriteModelFile = ParseBool("overwriteModel", args, ref i);
						break;

					default:
						if (arg.StartsWith("-"))
						{
							throw new ModelConverterException($"Unknown argument '{arg}'.");
						}

						if (string.IsNullOrEmpty(options.InputFile))
						{
							options.InputFile = arg;
						}
						else if (string.IsNullOrEmpty(options.OutputFile))
						{
							if (!arg.EndsWith(".drm", StringComparison.OrdinalIgnoreCase) && !arg.EndsWith(".jdrm", StringComparison.OrdinalIgnoreCase))
							{
								throw new ModelConverterException($"Output file should have either '.drm' or '.jdrm' extension. Current value = '{arg}'.");
							}

							options.OutputFile = arg;
						}
						else
						{
							throw new ModelConverterException($"Unknown argument '{arg}'.");
						}

						break;
				}
			}

			if (string.IsNullOrEmpty(options.InputFile))
			{
				throw new ModelConverterException("Input file is not specified.");
			}

			if (string.IsNullOrEmpty(options.OutputFile))
			{
				throw new ModelConverterException("Output file is not specified.");
			}

			Log(options.ToString());

			var converter = new Converter();
			converter.Convert(options);
		}


		static int Main(string[] args)
		{
			try
			{
				Process(args);
			}
			catch (ModelConverterException ex)
			{
				Log($"Argument error: {ex.Message}");
				return 2;
			}
			catch (Exception ex)
			{
				Log(ex.ToString());
				return 1;
			}

			return 0;
		}
	}
}


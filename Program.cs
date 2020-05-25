using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using McMaster.Extensions.CommandLineUtils;

namespace HexPrintFile
{
    internal static class Program
    {
        // Return Code
        private enum ReturnCode
        {
            [Description("Success")]
            OK = 0,
            [Description("File not found")]
            FILE_NOT_FOUND = 1,
            [Description("Satrt byte greater than the length of the file")]
            START_LARGER_THAN_LENGTH = 2,
            [Description("End byte greater than or equal to the length of the file")]
            END_BYTE_BEYOND_LENGTH = 3,
            [Description("General error displaying the data")]
            GENERAL_ERROR = 4,
            [Description("Cannot use opptions -c and -e together")]
            OPTION_C_AND_E = 5,
            [Description("Start byte is less than zero")]
            START_LESS_ZERO = 6,
            [Description("End byte less than start byte")]
            END_LESS_THAN_START = 7,
            [Description("Command line parsing exception")]
            COMMAND_EXCEPTION = 8,
            [Description("Start byte is less than one and index from one mode enabled")]
            START_LESS_ONE = 9,
            [Description("Read block size cannot be less than 4 bytes")]
            CHUNK_LESS_THAN_MINIMUM = 10,
            [Description("Read block size cannot be more than 64 bytes")]
            CHUNK_MORE_THAN_MAXIMUM = 11,
        }
        
        // Read in 16 byte chunks
        private const int ChunkSize = 16;

        private static readonly string[] ControlChars =
        {
            "<NUL>" , "<SOH>" , "<STX>" , "<ETX>" ,
            "<EOT>" , "<ENQ>" , "<ACK>" , "<BEL>" ,
            "<BS>"  , "<HT>"  , "<LF>"  , "<VT>"  ,
            "<FF>"  , "<CR>"  , "<SO>"  , "<SI>"  ,
            "<DLE>" , "<DC1>" , "<DC2>" , "<DC3>" ,
            "<DC4>" , "<NAK>" , "<SYN>" , "<ETB>" ,
            "<CAN>" , "<EM>"  , "<SUB>" , "<ESC>" ,
            "<FS>"  , "<GS>"  , "<RS>"  , "<US>"  ,
        } ;

        /// <summary>
        /// Main program access point
        /// </summary>
        /// <param name="args">Program arguments</param>
        /// <returns>Error code, 0 on success</returns>
        private static int Main(string[] args)
        {
            // Get Build Information
            var buildDate = Assembly.GetExecutingAssembly().GetLinkerTimestampUtc().ToLocalTime();
            var buildDateString = $"{buildDate.Year.ToString("0000")}-{buildDate.Month.ToString("00")}-{buildDate.Day.ToString("00")} {buildDate.Hour.ToString("00")}:{buildDate.Minute.ToString("00")}:{buildDate.Second.ToString("00")}";
            var buildVersion = Assembly.GetEntryAssembly()?.GetName().Version;
            var buildVersionString = $"{buildVersion.Major}.{buildVersion.Minor}.{buildVersion.Build}";

            var app = new CommandLineApplication
            {
                Name = "HexPrintFile",
                Description = "A tool to print the hex representation of a file, or portion of a file",
                ExtendedHelpText = $@"
> ----------------------------------------------------
> Created by Mark Young
> github.com/tip2tail
> ----------------------------------------------------
> Return Codes
{GetReturnCodeDescriptions("> ")}
> ----------------------------------------------------
> Version:   {buildVersionString}
> Buid Date: {buildDateString}
> ----------------------------------------------------
",
            };

            // Setup
            app.HelpOption(inherited: true);
            app.UnrecognizedArgumentHandling = UnrecognizedArgumentHandling.Throw;
            
            // File is always required, the others are optional
            var fileName = app.Argument("file", "File to hex print").IsRequired();
            var opStartByte = app.Option<long>("-s|--start-byte <BYTE>",
                "Byte to start reading from. 0 if excluded.  Counted from index 0.",
                CommandOptionType.SingleValue);
            var opEndByte = app.Option<long>("-e|--end-byte <BYTE>",
                "Byte to read to (inclusive). Reads to EOF if excluded.  Counted from index 0. Cannot be used with -c.",
                CommandOptionType.SingleValue);
            var opCountBytes = app.Option<long>("-c|--count-bytes <COUNT>",
                "Returns X bytes from start byte (if provided). Cannot be used with -e.",
                CommandOptionType.SingleValue);
            var opChunkBytes = app.Option<uint>("-r|--read-chunk-size <BYTES>",
                "Sets the chunk size.  Minimum 4, Maximum 64.  Default is 16.",
                CommandOptionType.SingleValue);
            var opUseExtendedChars = app.Option<bool>("-x|--extended",
                "Use extended ASCII characters in output.",
                CommandOptionType.NoValue);
            var opIndexFromOne = app.Option<bool>("-o|--index-from-one",
                "Index the bytes from 1 rather than 0.",
                CommandOptionType.NoValue);

            // Execution logic
            app.OnExecute(() =>
            {
                
                // Verify that the file is valid
                if (!File.Exists(fileName.Value))
                {
                    Console.WriteLine("ERROR: File not found.");
                    return (int)ReturnCode.FILE_NOT_FOUND;    // File not found
                }
                if (opEndByte.HasValue() && opCountBytes.HasValue())
                {
                    Console.WriteLine("ERROR: Cannot use -c and -e together.");
                    return (int)ReturnCode.OPTION_C_AND_E;    // Cannot use -c and -e together
                }
                
                // Get the length in bytes
                var fileSize = (new FileInfo(fileName.Value)).Length;

                // Index from one?
                var indexFromOne = opIndexFromOne.HasValue();

                // Set the chunk size
                uint chunkSize = 16;
                if (opChunkBytes.HasValue())
                {
                    if (opChunkBytes.ParsedValue < 4)
                    {
                        Console.WriteLine("ERROR: Read block size cannot be less than 4 bytes.");
                        return (int)ReturnCode.CHUNK_LESS_THAN_MINIMUM;
                    }
                    if (opChunkBytes.ParsedValue > 64)
                    {
                        Console.WriteLine("ERROR: Read block size cannot be greater than 64 bytes.");
                        return (int)ReturnCode.CHUNK_MORE_THAN_MAXIMUM;
                    }
                    chunkSize = opChunkBytes.ParsedValue;
                }

                // Validate if we have a start and end
                long startAt = 0;
                if (opStartByte.HasValue())
                {
                    if (opStartByte.ParsedValue < 0)
                    {
                        Console.WriteLine("ERROR: Start byte cannot be less than zero.");
                        return (int)ReturnCode.START_LESS_ZERO;
                    }
                    if (indexFromOne && opStartByte.ParsedValue < 1)
                    {
                        Console.WriteLine("ERROR: Start byte cannot be less than one (when indexed from one).");
                        return (int)ReturnCode.START_LESS_ONE;
                    }
                    if (opStartByte.ParsedValue >= fileSize)
                    {
                        Console.WriteLine("ERROR: Start byte cannot be greater than the length of the file.");
                        return (int)ReturnCode.START_LARGER_THAN_LENGTH;
                    }

                    startAt = opStartByte.ParsedValue;
                }

                long endAt = fileSize - 1;
                if (opCountBytes.HasValue())
                {
                    if (indexFromOne) {
                        endAt = Math.Min(startAt + opCountBytes.ParsedValue, fileSize);
                    } else {
                        endAt = Math.Min(startAt + opCountBytes.ParsedValue, (fileSize - 1));
                    }
                }
                else if (opEndByte.HasValue())
                {
                    if (indexFromOne)
                    {
                        if (opEndByte.ParsedValue > fileSize)
                        {
                            Console.WriteLine("ERROR: End byte cannot be greater than the length of the file.");
                            return (int)ReturnCode.END_BYTE_BEYOND_LENGTH;
                        }
                    }
                    else
                    {
                        if (opEndByte.ParsedValue >= fileSize)
                        {
                            Console.WriteLine("ERROR: End byte cannot be greater than the length of the file.");
                            return (int)ReturnCode.END_BYTE_BEYOND_LENGTH;
                        }
                    }
                    if (opEndByte.ParsedValue < startAt)
                    {
                        Console.WriteLine("ERROR: End byte must be greater than or equal to the start byte.");
                        return (int)ReturnCode.END_LESS_THAN_START;
                    }

                    endAt = opEndByte.ParsedValue;
                }

                // Extended output
                var useExtended = opUseExtendedChars.HasValue();
                
                // We have validated our inputs, pass for processing
                if (ExecuteHexPrint(fileName.Value, startAt, endAt, fileSize, useExtended, indexFromOne, chunkSize)) return 0;
                
                Console.WriteLine("ERROR: Cannot display data.");
                return (int)ReturnCode.GENERAL_ERROR;
            });

            try {
                return app.Execute(args);
            }
            catch (CommandParsingException ex) {
                Console.WriteLine("EXCEPTION: Error when parsing the command line: " + ex.Message);
                return (int)ReturnCode.COMMAND_EXCEPTION;
            }
        }

        /// <summary>
        /// Execute the HEX Print
        /// </summary>
        /// <param name="fileName">File to read</param>
        /// <param name="startAt">Starting byte</param>
        /// <param name="endAt">End byte</param>
        /// <param name="fileSize">Full file size in bytes</param>
        /// <param name="useExtended">Use extended raw text output</param>
        /// <param name="indexFromOne">Should the counts be indexed from 1 or 0</param>
        /// <param name="chunkSize">Chunk size to read data in</param>
        /// <returns>TRUE on success</returns>
        private static bool ExecuteHexPrint(string fileName,
                                            long startAt,
                                            long endAt,
                                            long fileSize, 
                                            bool useExtended,
                                            bool indexFromOne,
                                            uint chunkSize)
        {
            int chunkSizeInt = Convert.ToInt32(chunkSize);
            var block = new byte[chunkSizeInt];
            var toRead = endAt - startAt;

            // We read in chunks (default 16)
            // So we need to work out how many chunks will give us our data
            // The result is that we may read up to (n-1) bytes extra - so be it!
            var actualBytesRead = Math.Min(((toRead - 1) | (chunkSize - 1)) + 1, fileSize);
            var chunksToRead = actualBytesRead / chunkSizeInt;

            var mainImage = StringLengthImage(endAt);
            var zeroImage = StringLengthImage(actualBytesRead);
            
            Console.WriteLine("HexPrintFile");
            Console.WriteLine("============");
            Console.WriteLine("");
            Console.WriteLine("Opening file:      " + fileName);
            Console.WriteLine("File size:         " + fileSize + " bytes");
            Console.WriteLine("");
            Console.WriteLine("Start At Byte:     " + (indexFromOne ? (startAt+1).ToString(mainImage) : startAt.ToString(mainImage)));
            Console.WriteLine("End At Byte:       " + (indexFromOne ? (endAt+1).ToString(mainImage) : endAt.ToString(mainImage)));
            Console.WriteLine("Bytes to read:     " + toRead.ToString());
            Console.WriteLine("Chunk size:        " + chunkSizeInt + " bytes");
            Console.WriteLine("Actual total read: " + actualBytesRead + " bytes");
            Console.WriteLine("Chunks:            " + chunksToRead);
            Console.WriteLine("");
            Console.WriteLine(indexFromOne
                ? "Note: Strings are all INDEXED FROM ONE"
                : "Note: Strings are all ZERO INDEXED");
            Console.WriteLine("");

            OutputHeader(chunkSizeInt, ref mainImage, zeroImage);
            
            try
            {
                // Open the file and seek the starting byte
                var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                if (indexFromOne) {
                    startAt++;
                }
                stream.Seek(startAt, SeekOrigin.Begin);

                var byteEnd = (startAt + chunkSizeInt) - 1;
                var fromZeroStart = 0;
                var fromZeroEnd = chunkSizeInt - 1;
                var chunksRead = 0;

                int bytesRead;
                while ((bytesRead = stream.Read(block, 0, chunkSizeInt)) > 0)
                {

                    if (chunksRead >= chunksToRead)
                    {
                        break;
                    }
                    
                    var hexString = BitConverter.ToString(block).Replace("-", " ");
                    var rawString = AsciiOctets2String(block, useExtended);
                    if (bytesRead != chunkSizeInt)
                    {
                        // To tidy this up remove the 00 bytes that we did not read
                        var endIndex = (bytesRead * 3) - 1;
                        hexString = hexString.Substring(0, endIndex);
                        hexString = hexString.PadRight((chunkSizeInt * 3) - 1);
                        rawString = rawString.Substring(0, bytesRead);
                        rawString = rawString.PadRight(chunkSizeInt);
                    }

                    string outputString;
                    if (indexFromOne)
                    {
                        outputString =
                            $"* {hexString} *   | {rawString} | {(startAt).ToString(mainImage)} -> {(byteEnd).ToString(mainImage)}  | {(fromZeroStart+1).ToString(zeroImage)} -> {(fromZeroEnd+1).ToString(zeroImage)}";
                    }
                    else
                    {
                        outputString =
                            $"* {hexString} *   | {rawString} | {startAt.ToString(mainImage)} -> {byteEnd.ToString(mainImage)}  | {fromZeroStart.ToString(zeroImage)} -> {fromZeroEnd.ToString(zeroImage)}";
                    }

                    Console.WriteLine(outputString);

                    startAt += chunkSizeInt;
                    byteEnd += chunkSizeInt;
                    fromZeroStart += chunkSizeInt;
                    fromZeroEnd += chunkSizeInt;
                    chunksRead++;
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("EXCEPTION: " + exception.Message);
                return false;
            }
            OutputHeader(chunkSizeInt, ref mainImage, zeroImage);
            return true;
        }

        /// <summary>
        /// Outputs the header for the HEX print
        /// </summary>
        /// <param name="chunkSize">Chunk size</param>
        /// <param name="mainImage" ref="true">Main byte range image</param>
        /// <param name="zeroImage">Zero count byte range image</param>
        private static void OutputHeader(int chunkSize, ref string mainImage, string zeroImage)
        {
            // Main heading minimum = 14
            if (mainImage.Length < 5) {
                mainImage = "00000";
            }
            var mainLen = ((2 * mainImage.Length) + 4) + 1;
            var mainHeading = "ACTUAL BYTES";
            while (mainHeading.Length < mainLen) {
                mainHeading += " ";
            }

            var zeroLen = (2 * zeroImage.Length) + 4;
            var zeroHeading = "READ COUNT";
            while (zeroHeading.Length < zeroLen) {
                zeroHeading += " ";
            }

            var dataHeading = " HEX DATA ".PadRight((chunkSize * 3) + 1);
            var rawHeading  = " RAW DATA ".PadRight(chunkSize + 2);

            var dataLine = $"*{dataHeading}*   |{rawHeading}| {mainHeading} | {zeroHeading} ";
            var equalsLine = string.Empty;
            while (equalsLine.Length < dataLine.Length) {
                equalsLine += "=";
            }

            Console.WriteLine(equalsLine);
            Console.WriteLine(dataLine);
            Console.WriteLine(equalsLine);
        }

        /// <summary>
        /// Converts a byte array to a string representation, converting any unprintable characters as required
        /// </summary>
        /// <param name="bytes">Array of bytes to convert</param>
        /// <param name="useExtendedOutput">If true, will output a text representation of the unprintable character.  Otherwise will display a box character.</param>
        /// <returns>string output</returns>
        private static string AsciiOctets2String(IReadOnlyCollection<byte> bytes, bool useExtendedOutput = false)
        {
            var sb = new StringBuilder(bytes.Count);
            foreach (var c in bytes.Select(b => (char)b))
            {
                if (useExtendedOutput)
                {
                    if (c < '\u0020')
                    {
                        sb.Append(ControlChars[c]);
                    }
                    else if (c == '\u007F')
                    {
                        sb.Append("<DEL>");
                    }
                    else if (c > '\u007F')
                    {
                        sb.AppendFormat(@"\u{0:X4}", (ushort) c);
                    }
                    else /* 0x20-0x7E */
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    // Replace anything that is outside the range that is printable with a little box character
                    var s = c.ToString();
                    s = Regex.Replace(s, @"\p{C}+", "■");
                    sb.Append(s);
                }
            }
            return sb.ToString() ;
        }

        /// <summary>
        /// Returns a ToString image for the number of digits in the given value
        /// </summary>
        /// <param name="value">Array of bytes to convert</param>
        /// <returns>string</returns>
        private static string StringLengthImage(long value)
        {
            var image = string.Empty;
            for (int i = 0; i < value.ToString().Length; i++) {
                image += "0";
            }
            return image;
        }

        /// <summary>
        /// Returns a string block showing all the values of the ReturnCode Enum
        /// </summary>
        /// <param name="lineStart">Characters to add at start of each line</param>
        /// <returns>string</returns>
        private static string GetReturnCodeDescriptions(string lineStart = null)
        {
            // Make sure this is not null
            if (string.IsNullOrEmpty(lineStart))
            {
                lineStart = string.Empty;
            }
            var retVal = string.Empty;
            foreach (ReturnCode retCode in (ReturnCode[])Enum.GetValues(typeof(ReturnCode)))
            {
                if (!retVal.Equals(string.Empty))
                {
                    // New line
                    retVal += Environment.NewLine;
                }
                var value = (int)retCode;
                var desc = retCode.GetDescription();
                retVal += $"{lineStart}{value}: {desc}";
            }
            return retVal;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;

// Reference the API
using ThingMagic;

namespace Firmware
{
    /// <summary>
    /// Sample program to load firmware
    /// </summary>
    class Firmware
    {
        static void usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [--fw] [firmware file/path] --erase/revert",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " --fw : Update the firmware",
                    " --erase : (Erase)Wipe device program memory before installing new firmware(applicable to n/w readers only)",
                    " --revert : (Revert)Wipe device configuration(applicable to n/w readers only)",
                    " Example: 1) Serial reader : 'tmr:///com4 --fw filename' or '-v tmr:///com4 --fw filename'",
                    "          2) Fixed reader : 'tmr:///com4 --fw filename --erase/revert' or '-v tmr:///com4 --fw filename --erase/revert'"
                }));
            Environment.Exit(1);
        }
        static void Main(string[] args)
        {
            String readerUri = null;
            String fwFilename = null;
            Boolean erase = false;
            Boolean revert = false;
            FileStream fileStream;
            Boolean argError = false;

            for (int nextarg = 0; nextarg < args.Length; nextarg++)
            {
                string arg = args[nextarg];
                if (arg.Equals("--erase"))
                {
                    erase = true;
                }
                else if (arg.Equals("--revert"))
                {
                    revert = true;
                }
                else if (arg.Equals("--fw"))
                {
                    fwFilename = args[++nextarg];
                }
                else if (null == readerUri)
                {
                    readerUri = arg;
                }
                else
                {
                    Console.WriteLine("Argument {0}:\"{1}\" is not recognized", nextarg, arg);
                    argError = true;
                }
            }
            if (null == readerUri)
            {
                Console.WriteLine("Reader URI not specified");
                argError = true;
            }

            if (null == fwFilename)
            {
                Console.WriteLine("Firmware filename not specified");
                argError = true;
            }

            if (argError)
            {
                usage();
            }

            try
            {
                // Create Reader object, connecting to physical device.
                // Wrap reader in a "using" block to get automatic
                // reader shutdown (using IDisposable interface).
                using (Reader r = Reader.Create(readerUri))
                {
                    //Uncomment this line to add default transport listener.
                    //r.Transport += r.SimpleTransportListener;
                    try
                    {
                        r.Connect();
                        if (Reader.Region.UNSPEC == (Reader.Region)r.ParamGet("/reader/region/id"))
                        {
                            Reader.Region[] supportedRegions = (Reader.Region[])r.ParamGet("/reader/region/supportedRegions");
                            if (supportedRegions.Length < 1)
                            {
                                throw new FAULT_INVALID_REGION_Exception();
                            }
                            r.ParamSet("/reader/region/id", supportedRegions[0]);
                        }
                    }
                    catch (FAULT_BL_INVALID_IMAGE_CRC_Exception)
                    {
                        Console.WriteLine("Current image failed CRC check. Proceeding to load firmware, anyway.");
                    }
                    catch (FAULT_BL_INVALID_APP_END_ADDR_Exception)
                    {
                        Console.WriteLine("The last word of the firmware image stored in the reader's "
                                      + "flash ROM does not have the correct address value. Proceeding to load firmware, anyway.");
                    }
                    catch (FAULT_TM_ASSERT_FAILED_Exception)
                    {
                        Console.WriteLine("Assert failed. Proceeding to load firmware, anyway.");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("The operation has timed out"))
                        {
                            Console.WriteLine("Connection to reader failed. Proceeding to load firmware, anyway.");
                        }
                        else
                        {
                            throw ex;
                        }
                    }

                    if (r is RqlReader || r is LlrpReader)
                    {
                        fileStream = new FileStream(fwFilename, FileMode.Open, FileAccess.Read);
                        FirmwareLoadOptions flo = new LlrpFirmwareLoadOptions(erase, revert);
                        Console.WriteLine("Loading Firmware....");
                        r.FirmwareLoad(fileStream, flo);
                    }
                    else if (r is SerialReader)
                    {
                        fileStream = new FileStream(fwFilename, FileMode.Open, FileAccess.Read);
                        Console.WriteLine("Loading Firmware....");
                        r.FirmwareLoad(fileStream);
                    }
                    string version = (string)r.ParamGet("/reader/version/software");
                    Console.WriteLine("Firmware load successful with version {0}", version);
                }
            }

            catch (ReaderException re)
            {
                Console.WriteLine("Error: " + re.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
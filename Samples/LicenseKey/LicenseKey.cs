using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;

namespace LicenseKey
{
    class LicenseKey
    {
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [--option <license operation>] [--key <licence key>]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " --option: to select the options(1.set 2.erase)",
                    " set: update given license e.g.,tmr:///COM4 --option set --key AB CD>",
                    " erase: erase existing license e.g.,tmr:///COM4 --option erase",
                    " Example: 'tmr:///com4 --option set --key 112233' or '-v tmr:///com4 --option set --key 112233'"
                }));
            Environment.Exit(1);
        }
        static StringBuilder licenseKey = new StringBuilder();
        static ThingMagic.Reader.LicenseOperation op = new ThingMagic.Reader.LicenseOperation();
        static List<string> licenseList = new List<string>();
        static List<byte> licensebyteList = new List<byte>();
        static byte[] byteLic = new byte[] { };
        static void Main(string[] args)
        {
            int k = 0;
            bool optionFound = false;
            bool keyFound = false;
            int keyIndex = 0;
            int keyLength = 0;
            // Program setup
            if (3 > args.Length)
            {
                Usage();
            }
            else
            {
                try
                {
                    for (k = 1; k < args.Length; k++)
                    {
                        /* check for license operation option */
                        if ((!optionFound) && (args[k].Equals("--option", StringComparison.CurrentCultureIgnoreCase)))
                        {
                            optionFound = true;
                            /* parse license option provided by user */
                            parseLicenseOperationOption(k, args);
                            k++;
                        }

                        /* check for license key */
                        else if ((!keyFound) && (args[k].Equals("--KEY", StringComparison.CurrentCultureIgnoreCase)))
                        {
                            keyFound = true;
                            keyIndex = k; /* Store the key index */

                            /* Calculate the license key length */
                            keyLength = calculateLicenseKeyLength(keyIndex, optionFound, args.Length, args);
                            k += keyLength;

                            if (keyLength != 0)
                            {
                                /* parse the license key */
                                byteLic = parseLicenseKey(keyIndex, keyLength, args);
                                op.key = byteLic;
                            }
                            else
                            {
                                Console.WriteLine("License key not found");
                                Usage();
                            }
                        }
                        else
                        {
                            Console.WriteLine("Arguments are not recognized");
                            Usage();
                        }
                    }

                    /* Error handling */
                    if (!optionFound)
                    {
                        Console.WriteLine("license operation option is not found");
                        Usage();
                    }
                    else if ((op.option == Reader.LicenseOption.SET_LICENSE_KEY) && (!keyFound))
                    {
                        Console.WriteLine("License key not found");
                        Usage();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error: " + e.Message);
                    Usage();
                }
            }

            try
            {
                // Create Reader object, connecting to physical device.
                // Wrap reader in a "using" block to get automatic
                // reader shutdown (using IDisposable interface).
                using (Reader r = Reader.Create(args[0]))
                {
                    //Uncomment this line to add default transport listener.
                    //r.Transport += r.SimpleTransportListener;

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

                    //Uncomment this to only "Set" licensekey.
                    //Console.WriteLine("License Update Started...");
                    //r.ParamSet("/reader/licensekey", byteLic);
                    //Console.WriteLine("License Update Successfull...");

                    // Manage License key param supports both setting and erasing the license.
                    Console.WriteLine("License Operation started...");
                    r.ParamSet("/reader/manageLicenseKey", op);
                    Console.WriteLine("License Operation succeeded...");
                    // Printing Supported Protocol
                    TagProtocol[] protocolList = (TagProtocol[])r.ParamGet("/reader/version/supportedProtocols");
                    Console.WriteLine("Supported Protocols : ");
                    foreach (TagProtocol tp in protocolList)
                    {
                        Console.WriteLine(tp.ToString());
                    }
                    Console.WriteLine("");
                    string model = (string)r.ParamGet("/reader/version/model").ToString();
                    if (model.Equals("M3e"))
                    {
                        SupportedTagFeatures tagFeatures;
                        foreach (TagProtocol proto in protocolList)
                        {
                            switch (proto)
                            {
                                case TagProtocol.ISO14443A:
                                    tagFeatures = (SupportedTagFeatures)r.ParamGet("/reader/iso14443a/supportedTagFeatures");
                                    Console.WriteLine("ISO14443A Tag Features: " + tagFeatures);
                                    break;
                                case TagProtocol.ISO15693:
                                    tagFeatures = (SupportedTagFeatures)r.ParamGet("/reader/iso15693/supportedTagFeatures");
                                    Console.WriteLine("ISO15693 Tag Features: " + tagFeatures);
                                    break;
                                case TagProtocol.LF125KHZ:
                                    tagFeatures = (SupportedTagFeatures)r.ParamGet("/reader/lf125khz/supportedTagFeatures");
                                    Console.WriteLine("LF125KHZ Tag Features: " + tagFeatures);
                                    break;
                                default:
                                    Console.WriteLine("No Tag Features are enabled");
                                    break;
                            }
                        }
                    }
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

        public static byte[] parseLicenseKey(int keyIndex, int keyLength, String[] args)
        {
            /* parse license option provided by user */
            int i;
            int keyStartPointer = keyIndex + 1;

            for (i = 0; i < keyLength; i++)
            {
                licenseKey.Append(args[keyStartPointer + i]);
            }

            licenseList.AddRange(licenseKey.ToString().Trim().Split(' '));
            string licenseKeyString = string.Join("", licenseList.ToArray());
            for (i = 0; i < licenseKey.Length; i += 2)
            {
                licensebyteList.Add(Convert.ToByte(licenseKeyString.Substring(i, 2), 16));
            }
            byteLic = licensebyteList.ToArray();
            return byteLic;
        }

        public static void parseLicenseOperationOption(int index, String[] argv)
        {
            /* parse license option provided by user */
            String argument = argv[index + 1];
            if (argument.Equals("SET", StringComparison.CurrentCultureIgnoreCase))
            {
                op.option = Reader.LicenseOption.SET_LICENSE_KEY;
            }
            else if (argument.Equals("ERASE", StringComparison.CurrentCultureIgnoreCase))
            {
                op.option = Reader.LicenseOption.ERASE_LICENSE_KEY;
            }
            else
            {
                Console.WriteLine("Unsupported license operation");
                Usage();
            }
        }

        public static int calculateLicenseKeyLength(int index, bool isOptionFound, int argCount, String[] argv)
        {
            int keyLen = 0;
            int nextIndex = 0;
            bool nextArgIsOption = false;
            if (isOptionFound)
            {
                keyLen = argCount - 4;
            }
            else
            {
                nextIndex = index + 1;
                for (; (index < argCount) && (!nextArgIsOption); index++, nextIndex++)
                {
                    if (argv[nextIndex].Equals("--option", StringComparison.CurrentCultureIgnoreCase))
                    {
                        nextArgIsOption = true;
                    }
                    else if ((nextIndex + 1) == argCount)
                    {
                        Console.WriteLine("License operation option is not found");
                        Usage();
                    }
                    else
                    {
                        keyLen++;
                    }
                }
                index--;
            }
            return keyLen;
        }
    }
}
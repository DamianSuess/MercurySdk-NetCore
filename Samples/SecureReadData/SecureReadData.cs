using System;
using System.Collections.Generic;
using System.Text;
// for Thread.Sleep
using System.Threading;

// Reference the API
using ThingMagic;

namespace SecureReadData
{
    /// <summary>
    /// Sample program that supports PSAM functionality
    /// </summary>
    class SecureReadData
    {
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [--ant n[,n...]]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " [--ant n[,n...]] : e.g., '--ant 1,2,..,n",
                    " Example: 'tmr:///com4' or 'tmr:///com4 --ant 1,2' or '-v tmr:///com4 --ant 1,2'"
            }));
            Environment.Exit(1);
        }
        static void Main(string[] args)
        {
            // Program setup
            if (1 > args.Length)
            {
                Usage();
            }
            int[] antennaList = null;
            for (int nextarg = 1; nextarg < args.Length; nextarg++)
            {
                string arg = args[nextarg];
                if (arg.Equals("--ant"))
                {
                    if (null != antennaList)
                    {
                        Console.WriteLine("Duplicate argument: --ant specified more than once");
                        Usage();
                    }
                    antennaList = ParseAntennaList(args, nextarg);
                    nextarg++;
                }
                else
                {
                    Console.WriteLine("Argument {0}:\"{1}\" is not recognized", nextarg, arg);
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
                    // Read Plan
                    byte length;
                    string model = (string)r.ParamGet("/reader/version/model");
                    if (r.isAntDetectEnabled(antennaList))
                    {
                        Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                        Usage();
                    }
                    if ("M6e".Equals(model)
                        || "M6e PRC".Equals(model)
                        || "M6e JIC".Equals(model)
                        || "M6e Micro".Equals(model)
                        || "Mercury6".Equals(model)
                        || "Astra-EX".Equals(model)
                        || "Sargas".Equals(model))
                    {
                        // Specifying the readLength = 0 will return full TID for any tag read in case of M6e varients, M6 and Astra-EX reader.
                        length = 0;
                    }
                    else
                    {
                        length = 2;
                    }
                    
                    // Embedded Secure Read Tag Operation - Standalone operation not supported
                    Gen2.Password password = new Gen2.Password(0);
                    Gen2.SecureReadData secureReadDataTagOp = new Gen2.SecureReadData(Gen2.Bank.TID, 0, (byte)0, Gen2.SecureTagType.HIGGS3, password); ;
                    SimpleReadPlan plan = new SimpleReadPlan(antennaList, TagProtocol.GEN2, null, secureReadDataTagOp, 1000);
                    r.ParamSet("/reader/read/plan", plan);

                    // Create and add tag listener
                    r.TagRead += delegate(Object sender, TagReadDataEventArgs e)
                    {
                        Console.WriteLine("Background read: " + e.TagReadData);
                        if (0 < e.TagReadData.Data.Length)
                        Console.WriteLine("Requested data: "+ByteFormat.ToHex(e.TagReadData.Data));
                    };

                    // Create and add read exception listener
                    r.ReadException += r_ReadException;

                    // Create and add read authenticate listener
                    r.ReadAuthentication += r_ReadAuthenticationListener;

                    // Search for tags in the background
                    r.StartReading();

                    Console.WriteLine("\r\n<Do other work here>\r\n");
                    Thread.Sleep(500);
                    Console.WriteLine("\r\n<Do other work here>\r\n");
                    Thread.Sleep(500);

                    r.StopReading();
                }
            }
            catch (ReaderException re)
            {
                Console.WriteLine("Error: " + re.Message);
                Console.Out.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }

        static void r_ReadAuthenticationListener(object sender, ReadAuthenticationEventArgs e)
        {
            // This example simply uses a precompiled password.
            // In real life, an algorithm or server would examine the tag ID contained in trd and return an appropriate password.
            Reader rd = e.TagReadData.Reader;
            uint accessPassword = 0;
            uint tagAccessPassword1 = 0x11223344;
            uint tagAccessPassword2 = 0x22222222;
            uint tagAccessPassword3 = 0x33333333;
            uint tagAccessPassword4 = 0x11111111;
            Console.WriteLine("Tag epc  : " + e.TagReadData.EpcString);
            int index = e.TagReadData.Epc.Length % 4;
            if (index == 0)
            {
                accessPassword = tagAccessPassword1;
            }
            else if (index == 1)
            {
                accessPassword = tagAccessPassword2;
            }
            else if (index == 2)
            {
                accessPassword = tagAccessPassword3;
            }
            else if (index == 3)
            {
                accessPassword = tagAccessPassword4;
            }
            // Set password
            rd.ParamSet("/reader/gen2/accessPassword", new Gen2.Password(accessPassword));
        }

        static void r_ReadException(object sender, ReaderExceptionEventArgs e)
        {
            Console.WriteLine("Error: " + e.ReaderException.Message);
        }

        #region ParseAntennaList

        private static int[] ParseAntennaList(IList<string> args, int argPosition)
        {
            int[] antennaList = null;
            try
            {
                string str = args[argPosition + 1];
                antennaList = Array.ConvertAll<string, int>(str.Split(','), int.Parse);
                if (antennaList.Length == 0)
                {
                    antennaList = null;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("Missing argument after args[{0:d}] \"{1}\"", argPosition, args[argPosition]);
                Usage();
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0}\"{1}\"", ex.Message, args[argPosition + 1]);
                Usage();
            }
            return antennaList;
        }

        #endregion

    }
}

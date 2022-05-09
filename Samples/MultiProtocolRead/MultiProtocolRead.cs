using System;
using System.Collections.Generic;
using System.Text;
// for Thread.Sleep
using System.Threading;

// Reference the API
using ThingMagic;

namespace MultiProtocolRead
{
    /// <summary>
    /// Sample program that perform multi protocol read
    /// </summary>
    class Program
    {
        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [--ant n[,n...]]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " [--ant n[,n...]] : e.g., '--ant 1,2,..,n",
                    " Example for UHF: 'tmr:///com4' or 'tmr:///com4 --ant 1,2' or '-v tmr:///com4 --ant 1,2'",
                    " Example for HF/LF: 'tmr:///com4'"
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
                    string model = (string)r.ParamGet("/reader/version/model").ToString();
                    if (!model.Equals("M3e"))
                    {
                        if (r.isAntDetectEnabled(antennaList))
                        {
                            Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                            Usage();
                        }
                    }
                    else
                    {
                        if (antennaList != null)
                        {
                            Console.WriteLine("Module doesn't support antenna input");
                            Usage();
                        }
                    }
                    List<ReadPlan> readPlans = new List<ReadPlan>();
                    TagProtocol[] protocolList = (TagProtocol[])r.ParamGet("/reader/version/supportedProtocols");

                    if (model.Equals("M3e"))
                    {
                        //Set the multiple protocols using /reader/protocolList param for dynamic protocol switching
                        r.ParamSet("/reader/protocolList", protocolList);

                        // If /reader/protocolList param is set, API ignores the protocol mentioned in readplan.
                        SimpleReadPlan plan = new SimpleReadPlan(antennaList, TagProtocol.ISO14443A, null, null, 1000);
                        r.ParamSet("/reader/read/plan", plan);
                    }
                    else
                    {
                        foreach (TagProtocol protocol in protocolList)
                        {
                            readPlans.Add(new SimpleReadPlan(antennaList, protocol, null, null, 10));
                        }
                        MultiReadPlan testMultiReadPlan = new MultiReadPlan(readPlans);
                        r.ParamSet("/reader/read/plan", testMultiReadPlan);
                    }
                    // Create and add tag listener
                    r.TagRead += delegate(Object sender, TagReadDataEventArgs e)
                    {
                        Console.WriteLine("Background read: " + e.TagReadData + " Protocol: " + e.TagReadData.Tag.Protocol);
                    };
                    // Create and add read exception listener
                    r.ReadException += new EventHandler<ReaderExceptionEventArgs>(r_ReadException);

                    // Search for tags in the background
                    r.StartReading();

                    Console.WriteLine("\r\n<Do other work here>\r\n");
                    Thread.Sleep(5000);
                    Console.WriteLine("\r\n<Do other work here>\r\n");
                    Thread.Sleep(500);

                    r.StopReading();
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

        // Read exception callback
        private static void r_ReadException(object sender, ReaderExceptionEventArgs e)
        {
            Console.WriteLine("Error: " + e.ReaderException.Message);
        }

    }
}
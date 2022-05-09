using System;
using System.Collections.Generic;
using System.Text;
// Reference the API
using ThingMagic;

namespace Bap
{
    /// <summary>
    /// Sample program to demonstrate BAP usage
    /// </summary>
    class Bap
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
                    if (r.isAntDetectEnabled(antennaList))
                    {
                        Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                        Usage();
                    }
                    // Create a simplereadplan which uses the antenna list created above
                    SimpleReadPlan plan = new SimpleReadPlan(antennaList, TagProtocol.GEN2);
                    // Set the created readplan
                    r.ParamSet("/reader/read/plan", plan);

                    Console.WriteLine("case 1: read by using the default bap parameter values");
                    Console.WriteLine("Get Bap default parameters");
                    Gen2.BAPParameters bap = (Gen2.BAPParameters)r.ParamGet("/reader/gen2/bap");
                    Console.WriteLine("powerupdelay : {0}  freqHopOfftimeUs {1}: " , bap.POWERUPDELAY , bap.FREQUENCYHOPOFFTIME);
                    // Read tags
                    TagReadData[] tagReads = r.Read(500);
                    // Print tag reads
                    foreach (TagReadData tr in tagReads)
                        Console.WriteLine(tr.ToString());

                    Console.WriteLine("case 2: read by setting the bap parameters");
                    bap.FREQUENCYHOPOFFTIME = 30000;
                    bap.POWERUPDELAY = 10000;
                    r.ParamSet("/reader/gen2/bap",bap);
                    Console.WriteLine("Get Bap  parameters");
                    bap = (Gen2.BAPParameters)r.ParamGet("/reader/gen2/bap");
                    Console.WriteLine("powerupdelay : {0}  freqHopOfftimeUs {1}: ", bap.POWERUPDELAY, bap.FREQUENCYHOPOFFTIME);
                    // Read tags
                    tagReads = r.Read(500);
                    // Print tag reads
                    foreach (TagReadData tr in tagReads)
                        Console.WriteLine(tr.ToString());

                    Console.WriteLine("case 3: read by disbling the bap option");
                    //initialize the with -1
                    //set the parameters to the module
                    r.ParamSet("/reader/gen2/bap", null);
                    Console.WriteLine("Get Bap  parameters");
                    bap = (Gen2.BAPParameters)r.ParamGet("/reader/gen2/bap");
                    Console.WriteLine("powerupdelay : {0}  freqHopOfftimeUs {1}: ", bap.POWERUPDELAY, bap.FREQUENCYHOPOFFTIME);
                    // Read tags
                    tagReads = r.Read(500);
                    // Print tag reads
                    foreach (TagReadData tr in tagReads)
                        Console.WriteLine(tr.ToString());
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

    }
}

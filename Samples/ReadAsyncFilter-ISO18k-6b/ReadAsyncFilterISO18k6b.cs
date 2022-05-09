using System;
using System.Collections.Generic;
using System.Text;
// for Thread.Sleep
using System.Threading;

// Reference the API
using ThingMagic;

namespace ReadAsyncFilterISO18k6b
{
    /// <summary>
    /// Sample program that reads all standard and non-standard ISO18k-6b in the background and prints the
    /// tags found.
    /// </summary>
    class ReadAsyncFilterISO18k6b
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

                    //Change ISO180006BTagOps flag to true for performing ISO180006b Tag Operation
                    //ISO180006b Tag Operation : WriteData, ReadData, LockTag
                    bool ISO180006BTagOps = false;
                    string EPCForTagOps = "";

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

                    //Set delimiter to 1
                    r.ParamSet("/reader/iso180006b/delimiter", Iso180006b.Delimiter.DELIMITER1);

                    // Read Plan
                    Iso180006b.Select filter = new Iso180006b.Select(false, Iso180006b.SelectOp.NOTEQUALS, 0, 0xff, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
                    SimpleReadPlan plan = new SimpleReadPlan(antennaList, TagProtocol.ISO180006B, filter, null, 1000);
                    r.ParamSet("/reader/read/plan", plan);

                    // Create and add tag listener
                    r.TagRead += delegate(Object sender, TagReadDataEventArgs e)
                     {
                         Console.WriteLine("Background read: " + e.TagReadData);
                         if (string.IsNullOrEmpty(EPCForTagOps))
                         {
                             EPCForTagOps = e.TagReadData.EpcString;
                         }
                     };

                    // Create and add read exception listener
                    r.ReadException += new EventHandler<ReaderExceptionEventArgs>(r_ReadException);
                    // Search for tags in the background
                    r.StartReading();

                    Console.WriteLine("\r\n<Do other work here>\r\n");
                    Thread.Sleep(500);
                    Console.WriteLine("\r\n<Do other work here>\r\n");
                    Thread.Sleep(500);

                    r.StopReading();

                    if (ISO180006BTagOps)
                    {
                        if (!String.IsNullOrEmpty(EPCForTagOps))
                        {
                            Console.WriteLine("\n****************ISO180006b Tag Operation********************");
                            //Use first antenna for operation
                            if (antennaList != null)
                                r.ParamSet("/reader/tagop/antenna", antennaList[0]);

                            // tag to be filtered
                            TagFilter filterToUse = new TagData(EPCForTagOps);

                            // write data to a particular address location of tag
                            byte address = 0x28;
                            byte[] writedata = new byte[] { 0xAA, 0x34, 0x56, 0x78 };
                            Iso180006b.WriteData writeOp = new Iso180006b.WriteData(address, writedata);
                            r.ExecuteTagOp(writeOp, filterToUse);
                            Console.WriteLine("Write Data Successfull");

                            //read  data from a specified memory location works only with tag data filter
                            Iso180006b.ReadData readtagop = new Iso180006b.ReadData(address, 4);
                            object data = r.ExecuteTagOp(readtagop, filterToUse);
                            Console.WriteLine("Read Data : " + BitConverter.ToString((byte[])data));

                            // Lock the tag at the specified address
                            // Uncomment Below Code to Perfrom Lock Operation
                            //Iso180006b.LockTag lockOp = new Iso180006b.LockTag(address);
                            //r.ExecuteTagOp(lockOp, filterToUse);
                            //Console.WriteLine("Lock Tag Successfull");
                        }
                        else
                        {
                            throw new Exception("No ISO180006B Tags found.");
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

        private static void r_ReadException(object sender, ReaderExceptionEventArgs e)
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

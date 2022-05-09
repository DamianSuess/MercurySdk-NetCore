using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;
using System.Threading;

namespace RegionConfiguration
{
    /// <summary>
    /// Sample program that reads tags for a fixed period of time (500ms)
    /// and prints the tags found.
    /// Set to OPEN region and customise accroding to the users inputs
    /// </summary>
    class RegionConfiguration
    {
        /*
         * these are default vaules for bahrain region 
         * Open region parameters to be set to achieve custom region
        */
        // enable LBT 
        public static bool lbtEnable = true;
        // Enable Dwell time
        public static bool DwellEnable = true;
        /* Min is 1 and Max 65535 values for Dwelltime
         * 0 is not valid for dwelltime
         */
        public static UInt16 DwellTime = 100;
        // Value should be between -128 to 0.
        public static Int16 LBTThreshold = -72;
        /* Set frequency hop table accordingly for a region.
         * This is a custom parameter and user can change the hop table as per his requirments
         * so that it can support as many custom region as possible
         * Below is an example hop table for region EU3.
         * based on the hop table next parameters (hop time, quantization step, min freq) are dependent
         */
        public static int[] hopTable = { 865700, 866300, 866900, 867500 };
        // user can set hop time as per hop table
        public static int hopTime = 3975;
        // user can set quantization step based on the hop table
        public static int QuantizationStep = 100000;
        // user can set quantization step based on the region and hop table
        public static int MinFreq = 865700;
        public static ConfigRegion region = 0;

        public enum ConfigRegion
        {
            DEFAULT = 0,
            BAHRAIN = 1,
            BRAZIL = 2
        }

        static void Usage()
        {
            Console.WriteLine(String.Join("\r\n", new string[] {
                    " Usage: "+"Please provide valid reader URL, such as: [-v] [reader-uri] [--ant n[,n...]] [--pow read_power]",
                    " -v : (Verbose)Turn on transport listener",
                    " reader-uri : e.g., 'tmr:///com4' or 'tmr:///dev/ttyS0/' or 'tmr://readerIP'",
                    " [--ant n[,n...]] : e.g., '--ant 1,2,..,n",
                    " [--pow read_power] : e.g, '--pow 2300'\n",
                    " Example: -v tmr:///com4 --ant 1 --region 1"
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
                else if (arg.Equals("--region"))
                {
                    region = (ConfigRegion)(Convert.ToInt32(args[++nextarg]));
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

                    if (r.isAntDetectEnabled(antennaList))
                    {
                        Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                        Usage();
                    }

                    //// Region configuration is only applicable for OPEN region
                    // Step - 1 Set OPEN Region
                    r.ParamSet("/reader/region/id", Reader.Region.OPEN);

                    //get the region from command line and set the defualt values accroding to the region selected
                    SetRegionParam(region);
                    if ((r.ParamGet("/reader/region/id")).ToString() == "OPEN")
                    {
                        // Step - 2 Set HOPTable
                        r.ParamSet("/reader/region/hopTable", hopTable);

                        /*
                         * To set individual parameters please use these commands till Dwelltime
                         */
                        // Step - 3 Set LBT enable
                        r.ParamSet("/reader/region/lbt/enable", lbtEnable);
                        // Step - 4 Set LBT threshold
                        if (lbtEnable)
                            r.ParamSet("/reader/region/lbtThreshold", LBTThreshold);
                        //Step - 5 Set dwell time enable

                        r.ParamSet("/reader/region/dwellTime/enable", DwellEnable);
                        //Step - 6 Set dwell time 
                        if (DwellEnable)
                            r.ParamSet("/reader/region/dwellTime", DwellTime);
                        // Step - 7 Set hop time
                        r.ParamSet("/reader/region/hopTime", hopTime);
                        // Step - 8 Set quantization step
                        r.ParamSet("/reader/region/quantizationStep", QuantizationStep);
                        // Step - 9 Set region minimum frequency
                        r.ParamSet("/reader/region/minimumFrequency", MinFreq);
                    }
                    /*
                     * Uncomment Step 10 through 12 to save OPEN region settings as persistent. 
                     */
                    ////Step 10 - Save configuration
                    //r.ParamSet("/reader/userConfig", new SerialReader.UserConfigOp(SerialReader.UserConfigOperation.SAVE));
                    //Console.WriteLine("User profile set option:save all configuration");
                    ////Step 11 - Restore configuration
                    //r.ParamSet("/reader/userConfig", new SerialReader.UserConfigOp(SerialReader.UserConfigOperation.RESTORE));
                    //Console.WriteLine("User profile set option:restore all configuration");
                    ////Step 12 - Verify configuration
                    //r.ParamSet("/reader/userConfig", new SerialReader.UserConfigOp(SerialReader.UserConfigOperation.VERIFY));
                    //Console.WriteLine("User profile set option:verify all configuration");

                    Console.WriteLine("Region Set " + r.ParamGet("/reader/region/id"));
                    // Get the parameter values 
                    lbtEnable = Convert.ToBoolean(r.ParamGet("/reader/region/lbt/enable"));
                    Console.WriteLine("LBT enable - " + lbtEnable);

                    Console.WriteLine("LBT threshold set to - " + r.ParamGet("/reader/region/lbtThreshold"));

                    DwellEnable = Convert.ToBoolean(r.ParamGet("/reader/region/dwellTime/enable"));
                    Console.WriteLine("Dwell time enable - " + DwellEnable);

                    Console.WriteLine("Dwell time set to - " + r.ParamGet("/reader/region/dwellTime"));

                    hopTime = Convert.ToInt32(r.ParamGet("/reader/region/hopTime"));
                    Console.WriteLine("Hop time set to - " + hopTime + "ms");

                    QuantizationStep = Convert.ToInt32(r.ParamGet("/reader/region/quantizationStep"));
                    Console.WriteLine("Quantization step set to - " + QuantizationStep / 1000 + "KHz");

                    MinFreq = Convert.ToInt32(r.ParamGet("/reader/region/minimumFrequency"));
                    Console.WriteLine("Minimum region frequency set to - " + MinFreq);

                    // Create a simplereadplan which uses the antenna list created above
                    SimpleReadPlan plan = new SimpleReadPlan(antennaList, TagProtocol.GEN2, null, null, 1000);
                    // Set the created readplan
                    r.ParamSet("/reader/read/plan", plan);

                    //// Read tags
                    TagReadData[] tagReads = r.Read(500);
                    // Print tag reads
                    foreach (TagReadData tr in tagReads)
                    {
                        Console.WriteLine("EPC: " + tr.EpcString);
                    }

                    //uncomment this ssection for reading async 
                    // Create and add tag listener
                    //r.TagRead += delegate(Object sender, TagReadDataEventArgs e)
                    //{
                    //    Console.WriteLine("Background read: " + e.TagReadData);
                    //};
                    //// Create and add read exception listener
                    //r.ReadException += new EventHandler<ReaderExceptionEventArgs>(r_ReadException);

                    //// Search for tags in the background
                    //r.StartReading();
                    //Console.WriteLine("\r\n<Do other work here>\r\n");
                    //Thread.Sleep(500);
                    //Console.WriteLine("\r\n<Do other work here>\r\n");
                    //Thread.Sleep(500);

                    //r.StopReading();
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
        private static void SetRegionParam(ConfigRegion region)
        {
            switch (region)
            {
                case ConfigRegion.BAHRAIN:
                    //default values will hold for Bharain region
                    Console.WriteLine("********Setting params for Bahrain region********" + "\n");
                    lbtEnable = true;
                    DwellEnable = true;
                    LBTThreshold = -72;
                    DwellTime = 100;
                    hopTime = 3975;
                    QuantizationStep = 100000;
                    MinFreq = 865700;
                    hopTable = new int[] { 865700, 866300, 866900, 867500 };
                    break;
                case ConfigRegion.BRAZIL:
                    Console.WriteLine("********Setting params for Brazil region********" + "\n");
                    lbtEnable = false;
                    DwellEnable = false;
                    hopTime = 375;
                    QuantizationStep = 250000;
                    MinFreq = 902750;
                    //hop table for Brazil region
                    hopTable = new int[]{925500,905000,906000,925000,922750,902750,921000,926000,921750,916000,
                                     920250,919000,917750,925250,906500,920500,926500,916250,904000,919250,
                                     923750,906750,917250,924000,923000,915750,926750,924250,904250,918500,
                                     923500,920000,924750,905500,903500,921500,916500,922000,924500,920750,
                                     917500,927000,922250,918750,906250,905250,903750,919750,904750,921250};
                    break;
                default:
                    //default values are set for Bahrain region
                    //user is allowed to set values for achieveing custom region configurations
                    Console.WriteLine("********Setting default region params********" + "\n");
                    lbtEnable = true;
                    DwellEnable = true;
                    LBTThreshold = -72;
                    DwellTime = 100;
                    hopTime = 3975;
                    QuantizationStep = 100000;
                    MinFreq = 865700;
                    hopTable = new int[] { 865700, 866300, 866900, 867500 };
                    break;
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


﻿using System;
using System.Collections.Generic;
using System.Text;
using ThingMagic;

namespace SL900AProjectSetSFEParamters
{
    class SL900ASetSFEParametersTest
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

        Reader reader;
        private static int[] antennaList = null;
        static void Main(string[] args)
        {
            Console.WriteLine("Test the SL900A Set SFE Parameters function in the Mercury API");
            if (1 > args.Length)
            {
                Usage();
            }
            else
            {
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
                //Create the test object
                SL900ASetSFEParametersTest test = new SL900ASetSFEParametersTest();
                //Pass the reader uri to the object
                test.run(args[0]);
            }
        }
        private void run(String reader_uri)
        {
            try
            {
                String PARAM_STR_REGION = "/reader/region/id";
                String PARAM_STR_SESSION = "/reader/gen2/session";
                String PARAM_STR_READPLAN = "/reader/read/plan";

                Console.WriteLine(String.Format("Connecting to {0}", reader_uri));
                //Create the reader
                reader = Reader.Create(reader_uri);

                try
                {
                    //Uncomment this line to add default transport listener.
                    //reader.Transport += reader.SimpleTransportListener;

                    //Connect to the reader
                    reader.Connect();

                    //Set the region to NA
                    if (Reader.Region.UNSPEC == (Reader.Region)reader.ParamGet(PARAM_STR_REGION))
                    {
                        Reader.Region[] supportedRegions = (Reader.Region[])reader.ParamGet("/reader/region/supportedRegions");
                        if (supportedRegions.Length < 1)
                        {
                            throw new FAULT_INVALID_REGION_Exception();
                        }
                        else
                        {
                            reader.ParamSet(PARAM_STR_REGION, supportedRegions[0]);
                        }
                    }
                    if (reader.isAntDetectEnabled(antennaList))
                    {
                        Console.WriteLine("Module doesn't has antenna detection support please provide antenna list");
                        Usage();
                    }
                    //Use first antenna for operation
                    if (antennaList != null)
                        reader.ParamSet("/reader/tagop/antenna", antennaList[0]);


                    //Set the session to session 0
                    reader.ParamSet(PARAM_STR_SESSION, Gen2.Session.S0);

                    //Get the region
                    Reader.Region region = (Reader.Region)reader.ParamGet(PARAM_STR_REGION);
                    Console.WriteLine("The current region is " + region);

                    //Get the session
                    Gen2.Session session = (Gen2.Session)reader.ParamGet(PARAM_STR_SESSION);
                    Console.WriteLine("The current session is " + session);

                    //Get the read plan
                    ReadPlan rp = (ReadPlan)reader.ParamGet(PARAM_STR_READPLAN);
                    Console.WriteLine("The current Read Plan is: " + rp);

                    //Create the Get Calibration Data tag operation
                    Gen2.IDS.SL900A.GetCalibrationData getCal_tagop = new Gen2.IDS.SL900A.GetCalibrationData();

                    //Use the Get Calibration Data (and SFE Parameters) tag op
                    Gen2.IDS.SL900A.CalSfe calSfe = (Gen2.IDS.SL900A.CalSfe)reader.ExecuteTagOp(getCal_tagop, null);

                    //Save the current Sfe to restore it to the tag after the test
                    Gen2.IDS.SL900A.SfeParameters restore_sfe = (Gen2.IDS.SL900A.SfeParameters)calSfe.Sfe;

                    //Display the Calibration (and SFE Parameters) Data
                    Console.WriteLine("Detected Calibration: " + calSfe);

                    //Set the Sfe Parameters to 0xBEEF (16 bits)
                    byte[] test_sfe_byte_array = new byte[2] { 0xBE, 0xEF };

                    Gen2.IDS.SL900A.SfeParameters test_sfe = new Gen2.IDS.SL900A.SfeParameters(test_sfe_byte_array, 0);

                    //Execute the Set Calibration Data command with test_cal to change its value 
                    reader.ExecuteTagOp(new Gen2.IDS.SL900A.SetSfeParameters(test_sfe), null);

                    //Use Get Calibration Data to retrieve the new Calibration (and SFE Parameters) from the tag
                    Gen2.IDS.SL900A.CalSfe verification_calSfe = (Gen2.IDS.SL900A.CalSfe)reader.ExecuteTagOp(getCal_tagop, null);

                    //Get the Sfe data from the CalSfe data
                    Gen2.IDS.SL900A.SfeParameters verification_sfe = (Gen2.IDS.SL900A.SfeParameters)verification_calSfe.Sfe;

                    //Ensure that the Calibration Data we set matches the current Calibration Data
                    Console.WriteLine("Set SFE Parameters Succeeded? " + test_sfe.ToString().Equals(verification_sfe.ToString()));

                    //Restore the starting SFE Parameters
                    reader.ExecuteTagOp(new Gen2.IDS.SL900A.SetSfeParameters(restore_sfe), null);

                    //Get CalSfe of the restored tag
                    Gen2.IDS.SL900A.CalSfe restored_calSfe = (Gen2.IDS.SL900A.CalSfe)reader.ExecuteTagOp(getCal_tagop, null);

                    //Make sure that CalSfe is now the same as it was before the test
                    Console.WriteLine("Restore Calibration Data Succeeded? " + calSfe.ToString().Equals(restored_calSfe.ToString()));
                }
                finally
                {
                    //Disconnect from the reader
                    reader.Destroy();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e.Message);
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

using System;
using System.Collections.Generic;
using System.Text;

// Reference the API
using ThingMagic;
using AEITagDecoder;

namespace AEITagDecoding
{
    /// <summary>
    /// Sample program that reads AEI ATA tags for a fixed period of time (500ms)
    /// and prints the tag details.
    /// </summary>
    class AEITagDecoding
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
                    SimpleReadPlan plan = new SimpleReadPlan(antennaList, TagProtocol.ATA, null, null, 1000);
                    // Set the created readplan
                    r.ParamSet("/reader/read/plan", plan);

                    // Read tags
                    TagReadData[] tagReads = r.Read(2000);
                    string EPCData = "";
                    // Print tag reads
                    if (tagReads.Length > 0)
                    {
                        foreach (TagReadData tr in tagReads)
                        {
                            if (string.IsNullOrEmpty(EPCData))
                                EPCData = tr.EpcString;
                            else
                                break;
                        }


                        AeiTag decodedTag = new AeiTag(EPCData);
                        Console.WriteLine("**********************AEI ATA Tag Details*******************************");
                        Console.WriteLine("EPC Tag Data             : " + EPCData);
                        if ((AeiTag.DataFormat)decodedTag.getDataFormat == (AeiTag.DataFormat.SIX_BIT_ASCII) && !decodedTag.IsHalfFrameTag)
                        {
                            string binstrvalue = BinaryStringToHexString(EPCData);
                            Console.WriteLine("Binary Format            : " + binstrvalue);
                            List<int> finalList = AeiTag.FromString(binstrvalue);
                            Console.Write("ASCII Format             : ");
                            foreach (int temp in finalList)
                                Console.Write(AeiTag.convertDecToSixBitAscii(temp).ToString());
                            Console.WriteLine();
                        }
                        else
                        {
                            if (decodedTag.IsFieldValid[AeiTag.TagField.EQUIPMENT_GROUP])
                            {
                                Console.WriteLine("Equipment Group          : " + ((AeiTag.EquipmentGroup)decodedTag.getEquipmentGroup).ToString());
                                if (((AeiTag.EquipmentGroup)decodedTag.getEquipmentGroup) == AeiTag.EquipmentGroup.RAILCAR)
                                {
                                    Console.WriteLine("Car #                    : " + decodedTag.getCarNumber.ToString());
                                    Console.WriteLine("Side Indicator           : " + (AeiTag.SideIndicator)decodedTag.getSide);
                                    Console.WriteLine("Length(dm)               : " + decodedTag.getLengthInDecimeters + "dm");
                                    if (!decodedTag.IsHalfFrameTag)
                                    {
                                        Console.WriteLine("Number of Axles          : " + decodedTag.getNumberOfAxles.ToString());
                                        Console.WriteLine("Bearing Type             : " + (AeiTag.BearingType)decodedTag.getBearing);
                                        Console.WriteLine("Platform ID              : " + (AeiTag.PlatformId)decodedTag.getPlatformId);
                                        Console.WriteLine("Spare                    : " + decodedTag.getSpare.ToString());
                                    }
                                }
                                else if (((AeiTag.EquipmentGroup)decodedTag.getEquipmentGroup) == AeiTag.EquipmentGroup.END_OF_TRAIN_DEVICE)
                                {
                                    Console.WriteLine("EOT #                    : " + decodedTag.getCarNumber.ToString());
                                    Console.WriteLine("EOT Type                 : " + decodedTag.getEOTType.ToString());
                                    Console.WriteLine("Side Indicator           : " + (AeiTag.SideIndicator)decodedTag.getSide);
                                    if (!decodedTag.IsHalfFrameTag)
                                        Console.WriteLine("Spare                    : " + decodedTag.getSpare.ToString());
                                }
                                else if (((AeiTag.EquipmentGroup)decodedTag.getEquipmentGroup) == AeiTag.EquipmentGroup.LOCOMOTIVE)
                                {
                                    Console.WriteLine("Car #                    : " + decodedTag.getCarNumber.ToString());
                                    Console.WriteLine("Side Indicator           : " + (AeiTag.SideIndicator)decodedTag.getSide);
                                    Console.WriteLine("Length(dm)               : " + decodedTag.getLengthInDecimeters + "dm");
                                    if (!decodedTag.IsHalfFrameTag)
                                    {
                                        Console.WriteLine("Number of Axles          : " + decodedTag.getNumberOfAxles.ToString());
                                        Console.WriteLine("Bearing Type             : " + (AeiTag.BearingType)decodedTag.getBearing);
                                        Console.WriteLine("Spare                    : " + decodedTag.getSpare.ToString());
                                    }
                                }
                                else if (((AeiTag.EquipmentGroup)decodedTag.getEquipmentGroup) == AeiTag.EquipmentGroup.INTERMODAL_CONTAINER)
                                {
                                    Console.WriteLine("Car #                    : " + decodedTag.getCarNumber.ToString());
                                    Console.WriteLine("Check Digit              : " + decodedTag.getCheckDigit);
                                    Console.WriteLine("Length (cm)              : " + decodedTag.getLengthCM + "cm");
                                    if (!decodedTag.IsHalfFrameTag)
                                    {
                                        Console.WriteLine("Height (cm)              : " + decodedTag.getHeight);
                                        Console.WriteLine("Width (cm)               : " + decodedTag.getWidth);
                                        Console.WriteLine("Container Type           : " + decodedTag.getContainerType.ToString());
                                        Console.WriteLine("Container MaxGross Weight: " + decodedTag.getMaxGrossWeight);
                                        Console.WriteLine("Container Tare Weight    : " + decodedTag.getTareWeight);
                                        Console.WriteLine("Spare                    : " + decodedTag.getSpare.ToString());
                                    }
                                }
                                else if (((AeiTag.EquipmentGroup)decodedTag.getEquipmentGroup) == AeiTag.EquipmentGroup.TRAILER)
                                {
                                    Console.WriteLine("Trailer #                : " + decodedTag.getTrailerNumer.ToString());
                                    Console.WriteLine("Trailer # in words       : " + decodedTag.getTrailerNumerString.ToString());
                                    if (!decodedTag.IsHalfFrameTag)
                                    {
                                        Console.WriteLine("Length (cm)              : " + decodedTag.getLengthCM.ToString() + " cm");
                                        Console.WriteLine("Width (cm)               : " + decodedTag.getWidth.ToString() + " cm");
                                        Console.WriteLine("Height                   : " + decodedTag.getHeight.ToString());
                                        Console.WriteLine("Tandem Width (cm)        : " + decodedTag.getTandemWidth.ToString() + " cm");
                                        Console.WriteLine("Type Details             : " + decodedTag.getTypeDetail.ToString());
                                        Console.WriteLine("Forward Extension        : " + decodedTag.getForwardExtension.ToString());
                                        Console.WriteLine("Tare Weight              : " + decodedTag.getTareWeight.ToString());
                                    }
                                }
                                else if (((AeiTag.EquipmentGroup)decodedTag.getEquipmentGroup) == AeiTag.EquipmentGroup.CHASSIS)
                                {
                                    Console.WriteLine("Chassis #                    : " + decodedTag.getCarNumber.ToString());
                                    Console.WriteLine("Type Details             : " + decodedTag.getTypeDetail.ToString());
                                    Console.WriteLine("Tare Weight              : " + decodedTag.getTareWeight.ToString());
                                    Console.WriteLine("Height                   : " + decodedTag.getHeight.ToString());
                                    if (!decodedTag.IsHalfFrameTag)
                                    {
                                        Console.WriteLine("Tandem Width (cm)        : " + decodedTag.getTandemWidth.ToString() + " cm");
                                        Console.WriteLine("Forward Extension        : " + decodedTag.getForwardExtension.ToString());
                                        Console.WriteLine("King Pin Setting         : " + decodedTag.getKingPinSetting.ToString());
                                        Console.WriteLine("Axle Spacing             : " + decodedTag.getAxleSpacing.ToString());
                                        Console.WriteLine("Running Gear Loc         : " + decodedTag.getRunningGearLoc.ToString());
                                        Console.WriteLine("Num Lengths              : " + decodedTag.getNumLengths.ToString());
                                        Console.WriteLine("Min Length               : " + decodedTag.getMinLength.ToString());
                                        Console.WriteLine("Max Length               : " + decodedTag.getMaxLength.ToString());
                                        Console.WriteLine("Spare                    : " + decodedTag.getSpare.ToString());
                                    }
                                }
                                else if (((AeiTag.EquipmentGroup)decodedTag.getEquipmentGroup) == AeiTag.EquipmentGroup.RAILCAR_COVER)
                                {
                                    Console.WriteLine("Car #                    : " + decodedTag.getCarNumber.ToString());
                                    Console.WriteLine("Side Indicator           : " + (AeiTag.SideIndicator)decodedTag.getSide);
                                    Console.WriteLine("Length (dm)              : " + decodedTag.getLengthInDecimeters.ToString());
                                    Console.WriteLine("Cover Type               : " + decodedTag.getCoverType.ToString());
                                    Console.WriteLine("Date Built               : " + decodedTag.getDateBuilt.ToString());
                                    if (!decodedTag.IsHalfFrameTag)
                                    {
                                        Console.WriteLine("Insulation               : " + decodedTag.getInsulation.ToString());
                                        Console.WriteLine("Fitting                  : " + decodedTag.getFitting.ToString());
                                        Console.WriteLine("Assoc RailCar Initial    : " + decodedTag.getAssocRailcarInitial.ToString());
                                        Console.WriteLine("Assoc RailCar Initial    : " + decodedTag.getAssocRailcarInitialString);
                                        Console.WriteLine("Assoc RailCar Num        : " + decodedTag.getAssocRailcarInitialNumber.ToString());
                                    }
                                }
                                else if (((AeiTag.EquipmentGroup)decodedTag.getEquipmentGroup) == AeiTag.EquipmentGroup.PASSIVE_ALARM_TAG)
                                {
                                    Console.WriteLine("Car #                    : " + decodedTag.getCarNumber.ToString());
                                    Console.WriteLine("Side Indicator           : " + (AeiTag.SideIndicator)decodedTag.getSide);
                                    Console.WriteLine("Length (dm)              : " + decodedTag.getLengthInDecimeters.ToString());
                                    if (!decodedTag.IsHalfFrameTag)
                                    {
                                        Console.WriteLine("Number of Axles          : " + decodedTag.getNumberOfAxles.ToString());
                                        Console.WriteLine("Bearing Type             : " + (AeiTag.BearingType)decodedTag.getBearing);
                                        Console.WriteLine("Platform ID              : " + (AeiTag.PlatformId)decodedTag.getPlatformId);
                                        Console.WriteLine("Alarm                    : " + decodedTag.getAlarm.ToString());
                                        Console.WriteLine("Spare                    : " + decodedTag.getSpare.ToString());
                                    }
                                }
                                else if (((AeiTag.EquipmentGroup)decodedTag.getEquipmentGroup) == AeiTag.EquipmentGroup.GENERATOR_SET)
                                {
                                    Console.WriteLine("Generator Set #          : " + decodedTag.getGenSetNumber.ToString());
                                    Console.WriteLine("Generator Set #          : " + decodedTag.getGenSetNumberString);
                                    Console.WriteLine("Mounting                 : " + decodedTag.getMouting.ToString());
                                    if (!decodedTag.IsHalfFrameTag)
                                    {
                                        Console.WriteLine("Tare Weight              : " + decodedTag.getTareWeight);
                                        Console.WriteLine("Fuel Capacity            : " + decodedTag.getFuelCapacity);
                                        Console.WriteLine("Voltage                  : " + decodedTag.getVoltage);
                                        Console.WriteLine("Spare                    : " + decodedTag.getSpare.ToString());
                                    }
                                }
                                else if (((AeiTag.EquipmentGroup)decodedTag.getEquipmentGroup) == AeiTag.EquipmentGroup.MULTIMODAL_EQUIPMENT)
                                {
                                    Console.WriteLine("Car #                    : " + decodedTag.getCarNumber.ToString());
                                    Console.WriteLine("Side Indicator           : " + (AeiTag.SideIndicator)decodedTag.getSide);
                                    Console.WriteLine("Length(dm)               : " + decodedTag.getLengthInDecimeters + "dm");
                                    if (!decodedTag.IsHalfFrameTag)
                                    {
                                        Console.WriteLine("Number of Axles          : " + decodedTag.getNumberOfAxles.ToString());
                                        Console.WriteLine("Bearing Type             : " + (AeiTag.BearingType)decodedTag.getBearing);
                                        Console.WriteLine("Platform ID              : " + (AeiTag.PlatformId)decodedTag.getPlatformId);
                                        Console.WriteLine("Type Detail              : " + decodedTag.getTypeDetail.ToString());
                                        Console.WriteLine("Spare                    : " + decodedTag.getSpare.ToString());
                                    }
                                }
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Tag Not Prased Correctly : " + EPCData);
                                Console.WriteLine(BinaryStringToHexString(EPCData));
                            }
                            Console.ResetColor();
                            Console.WriteLine("Equipment Initial        : " + decodedTag.getEquipmentInitialString);
                            if (decodedTag.IsFieldValid[AeiTag.TagField.TAG_TYPE])
                                Console.WriteLine("Tag Type                 : " + (AeiTag.TagType)decodedTag.getTagType);
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Tag Type                 : " + Convert.ToString(decodedTag.getTagType, 2).PadLeft(2, '0') + " (Not a vaild Tag Type)");
                            }
                            Console.ResetColor();
                            if (!decodedTag.IsHalfFrameTag)
                            {
                                if (decodedTag.IsFieldValid[AeiTag.TagField.DATA_FORMAT])
                                    Console.WriteLine("Data Format Code         : " + (AeiTag.DataFormat)decodedTag.getDataFormat);
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Data Format Code         : " + Convert.ToString(decodedTag.getDataFormat, 2).PadLeft(6, '0') + " (Not a vaild Data Format)");
                                }
                            }
                            Console.ResetColor();
                        }
                    }
                    else
                    {
                        Console.WriteLine("Error : No ATA Tags Read.");

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

        public static string BinaryStringToHexString(string binary)
        {
            StringBuilder result = new StringBuilder(binary.Length / 8 + 1);
            int mod4Len = binary.Length % 8;
            if (mod4Len != 0)
            {
                // pad to length multiple of 8
                binary = binary.PadLeft(((binary.Length / 8) + 1) * 8, '0');
            }

            for (int i = 0; i < binary.Length; i += 1)
            {
                string eightBits = binary.Substring(i, 1);
                result.Append(Convert.ToString(Convert.ToInt64(eightBits, 16), 2).PadLeft(4, '0'));
            }

            return result.ToString();
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

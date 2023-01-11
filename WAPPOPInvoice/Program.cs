using DDay.iCal;
using Sage.Accounting.TradeLedger;
using Sicon.Sage200.WAP.AddonPackage.Objects;
using System;
using System.Configuration;

namespace WAPPOPInvoice
{
    /// <summary>
    /// Main program
    /// </summary>
    internal class Program
    {
        private static readonly ConsoleColor DEFAULT_FORECOLOR;

        /// <summary>
        /// Static Constructor
        /// </summary>
        static Program()
        {
            try
            {
                DEFAULT_FORECOLOR = Console.ForegroundColor;
            }
            catch (Exception e)
            {
                LogError(e);
            }
        }

        /// <summary>
        /// Main entry point
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            try
            {
                Console.Clear();

                LogGeneral("Resolving Sage Assemblies...");

                //Resolve all sage assemblies
                AssemblyResolver.ResolveAssemblies();

                LogGeneral("Sage Assemblies Resolved.");

                string companyName = ConfigurationManager.AppSettings["SageCompanyName"];

                //Syncronises any addon dlls from the server to local app data for the current user account (so addon dlls are in the same directory,
                //if your app is outside of local app data you may need to pull the assemblies in from the sage directory in IIS)
                //This also ensure any X-Table extensions are loaded
                bool syncroniseClientFiles = true;

                //Create client initators ensures initiators in any addons are set up, including Sage Manufacturing
                bool createClientInitiators = true;

                LogInfo($"Attempting to connect to company '{companyName}'.");

                //Connect to Sage 200 using the company name, and ensuring Syncronise client files and create client initiators is true
                using (SageConnection sageConnection = new SageConnection(companyName, syncroniseClientFiles, createClientInitiators))
                {
                    LogSuccess($"Connected to '{companyName}'.");

                    Program.CreateAndMatchPO();

                    LogGeneral($"Disconnecting from '{companyName}'.");
                }

                LogGeneral($"Disconnected from '{companyName}'.");

                LogGeneral($"Shutting Down.");

                LogInfo("Press any key to exit.");

                Console.ReadKey();
            }
            catch (Exception e)
            {
                LogError(e);

                Console.ReadKey();
            }
        }

        /// <summary>
        /// Creates a sales order
        /// </summary>
        private static void CreateAndMatchPO()
        {
            try
            {
                LogGeneral("Creating Purchase Order...");

                using (Sage.Accounting.POP.POPOrder popOrder = new Sage.Accounting.POP.POPOrder())
                {
                    popOrder.Supplier = Sage.Accounting.PurchaseLedger.SupplierFactory.Factory.Fetch("ATL001");

                    popOrder.Update();

                    Sage.Accounting.POP.POPFreeTextLine line = new Sage.Accounting.POP.POPFreeTextLine();

                    line.POPOrderReturn = popOrder;
                    line.ItemDescription = "Test Line";
                    line.UnitBuyingPrice = 100m;
                    line.ConfirmationIntentType = Sage.Accounting.POP.POPConfirmationIntentEnum.Confirm;
                    line.Post();

                    popOrder.CalculateValues();
                    popOrder.Post();

                    LogSuccess($"Purchase Order '{popOrder.DocumentNo}' posted successfully.");

                    LogInfo("Posting Invoice...");

                    using (Sage.Accounting.POP.POPMatchInvCredCoordinator coordinator = new Sage.Accounting.POP.POPMatchInvCredCoordinator())
                    {
                        coordinator.MatchInvoices = true;
                        coordinator.Supplier = popOrder.Supplier;
                        coordinator.OrderReturn = popOrder;
                        coordinator.PopulateInvCredItems();

                        LogInfo("Checking WAP Variance...");

                        Sicon.Sage200.WAP.AddonPackage.Objects.Configs.WAPSettings.UpdateConfiguration();

                        using (Sicon.Sage200.WAP.AddonPackage.Objects.Objects.Instruments.POPInvoiceVarianceInstrument varianceInstrument = new Sicon.Sage200.WAP.AddonPackage.Objects.Objects.Instruments.POPInvoiceVarianceInstrument(coordinator))
                        {
                            varianceInstrument.ValidationErrorOccured += message => Program.LogWarning(message);

                            varianceInstrument.Update();

                            if (!string.IsNullOrEmpty(varianceInstrument.VarianceMessage) && varianceInstrument.HasVariance)
                                varianceInstrument.VarianceComments = "Additional Comments";


                            Sage.Accounting.PurchaseLedger.PendingPurchaseBatchEntry entry = coordinator.Post();

                            if (entry?.BatchItems?.First is Sage.Accounting.TradeLedger.IPendingTradingBatchItem pendingItem
                                && pendingItem?.TradeInstrument is Sage.Accounting.PurchaseLedger.PurchaseNotificationInstrument invoiceInstrument)
                            {
                                LogSuccess($"Invoice '{(invoiceInstrument?.ActualPostedAccountEntry?.URN ?? invoiceInstrument?.ActualPendingAccountEntry?.URN)}' Posted Successfully");

                                using (Sicon.Sage200.WAP.AddonPackage.Objects.SiconWAPInvExtra invExtra = varianceInstrument.WAPInvoiceExtra)
                                {
                                    if(invExtra == null)
                                    {
                                        LogWarning("There was no variance for the invoice.");
                                        return;
                                    }

                                    // All the below will crash due to NULL siconWAPExtra
                                    invExtra.URN = (int)pendingItem.TradeInstrument.PostingReference.URN;
                                    invExtra.UpdatedDate = Sage.Common.Clock.Now;
                                    invExtra.UpdatedUser = Sage.Accounting.Application.ActiveUserName;
                                    invExtra.Urgent = false; //Optionally all you can mark invoices as urgent here
                                    invExtra.WAPNotes = string.Empty; //Optionally you can add some additional notes to send to WAP here.

                                    //Save the changes to the Sage database
                                    invExtra.Update();

                                }
                            }
                        }
                    }

                    LogSuccess($"Purchase Order '{popOrder.DocumentNo}' posted successfully.");
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        #region Logging

        /// <summary>
        /// Logs a Warning
        /// </summary>
        /// <param name="message">The Message</param>
        private static void LogWarning(string message) => Log($"Warning: {message}", ConsoleColor.Yellow);

        /// <summary>
        /// Logs a Success Message
        /// </summary>
        /// <param name="message">The Message</param>
        private static void LogSuccess(string message) => Log($"Success: {message}", ConsoleColor.Green);

        /// <summary>
        /// Logs an Error Message
        /// </summary>
        /// <param name="e">The Exception</param>
        private static void LogError(Exception e) => Log($"Error: {e}", ConsoleColor.Red);

        /// <summary>
        /// Logs an Info Message
        /// </summary>
        /// <param name="message">The Message</param>
        private static void LogInfo(string message) => Log($"Info: {message}", ConsoleColor.Cyan);

        /// <summary>
        /// Logs an Info Message
        /// </summary>
        /// <param name="message">The Message</param>
        private static void LogGeneral(string message) => Log($"Info: {message}", null);

        /// <summary>
        /// Writes a message to the console
        /// </summary>
        /// <param name="message">The Message</param>
        /// <param name="color">The Color</param>
        private static void Log(string message, ConsoleColor? color = null)
        {
            Console.ForegroundColor = color ?? DEFAULT_FORECOLOR;
            Console.WriteLine($"{DateTime.Now}: {message}");
            Console.ForegroundColor = DEFAULT_FORECOLOR;
        }

        #endregion Logging
    }
}

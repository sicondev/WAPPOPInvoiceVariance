using Sage.Common.Contexts;
using Sage.MMS.SAA.Client;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WAPPOPInvoice
{
    /// <summary>
    /// Class to handle connecting to Sage
    /// </summary>
    internal class SageConnection : IDisposable
    {
        private Guid _sessionGuid = Guid.Empty;
        private bool _syncroniseClientFiles = true;
        private bool _createClientInitiators = true;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="companyName">The Company Names</param>
        /// <param name="syncroniseClientFiles"><Whether to syncronise the client files</param>
        /// <param name="createClientInitiators">Whether to create the client initiators</param>
        internal SageConnection(string companyName = null, bool syncroniseClientFiles = true, bool createClientInitiators = true)
        {
            try
            {
                _syncroniseClientFiles = syncroniseClientFiles;
                _createClientInitiators = createClientInitiators;

                if (string.IsNullOrEmpty(companyName))
                    companyName = ConfigurationManager.AppSettings["SageCompanyName"];

                LogonFlags logonFlags = new LogonFlags();
                logonFlags.UnattendedMode = true;

                List<Company> companies = SAAClientAPI.CompaniesGetAll();

                Company targetCompany = (from Company company
                                        in companies
                                         where String.Compare(company.CompanyName, companyName, true) == 0
                                         select company)
                                        .FirstOrDefault();

                if (targetCompany == null)
                    throw new ArgumentException($"Could not find Company '{companyName}'.");

                try
                {
                    SAAClientAPI.Logon(logonFlags, SessionSourceEnum.Desktop);
                }
                catch (Exception ex)
                {
                    throw new ApplicationException($"Failed to open new session for Company with Company Name '{companyName}'.", ex);
                }

                //Check whether to sync client files and load bespoke addins
                if (_syncroniseClientFiles)
                {
                    AddOnManagerSingleton.Instance.SynchroniseClientFiles();

                    //Invoke InitialiseObjetStoreMetaData to ensure any bespoke addin fields from other addins are loaded
                    typeof(Sage.Accounting.Application).GetMethod("InitialiseObjectStoreMetaData", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);
                }

                //Connect to the company
                SAAClientAPI.ConnectCompany(targetCompany);

                //Create client initaliators so it hooks up manufacting and other modules that are installed
                if (_createClientInitiators)
                    typeof(Sage.Accounting.Application).GetMethod("CreateClientInitiators", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, null);

                _sessionGuid = new Guid(SessionContextValues.SessionID);

                SAAClientAPI.SetSessionContext(SessionContextValues.SessionID);
            }
            catch (Exception)
            {

                throw;
            }
        }

        /// <summary>
        /// Disposes of the connection
        /// </summary>
        public void Dispose()
        {
            try
            {
                try
                {
                    SAAClientAPI.SetSessionContext(_sessionGuid.ToString());

                    //Disconnect the company
                    SAAClientAPI.DisconnectCompany();
                }
                catch (Sage.Common.Exceptions.SessionExpiredException) { }
                catch (Exception ex) when (String.Compare(ex.Message, "Session has expired. Please close the application.") == 0) { }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    //Log off from Sage
                    SAAClientAPI.Logoff();

                    _sessionGuid = Guid.Empty;
                }
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}

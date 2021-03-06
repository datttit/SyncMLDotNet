using System;
using System.Xml.Linq;
using System.Diagnostics;
using Fonlow.SyncML.Common;
using Fonlow.SyncML.Elements;


namespace Fonlow.SyncML
{
    /// <summary>
    /// Process basic lonon message with authentication and sync request
    /// </summary>
    internal class LogOnMessage : SyncMLSyncMessageBase
    {
        public LogOnMessage(SyncMLFacade facade)
            : base(facade)
        {
        }

        private bool loggedOn;
        /// <summary>
        /// Mark whether the server had accept the credential from the client.
        /// </summary>
        public bool LoggedOn
        {
            get { return loggedOn; }
            set
            {
                loggedOn = value;
                if (loggedOn)
                {
                    Facade.AuthenticationTypeOfNextMessage = SyncMLAuthenticationType.None;
                }
            }
        }

        public override void Send()
        {
            CreateSyncRequestMessage(Facade.SyncType);
            CommandAndStatusRegister.Add(
             AddGetDeviceInfo(ClientSyncML));
            CommandAndStatusRegister.Add(
             AddPutDeviceInfo(ClientSyncML));
            ClientSyncML.Body.MarkFinal();
            Debug.WriteLine("Sending LogOn:" + ClientSyncML.Xml.ToString());

            string responseText = Facade.Connections.GetResponseText(ClientSyncML.Xml.ToString(SaveOptions.DisableFormatting));
            if (String.IsNullOrEmpty(responseText))
                return;

            Facade.CurrentMsgID++;
            ProcessResponse(responseText);
        }

        public SyncMLAuthenticationStatus AuthenticationStatus
        { get; private set; }

        protected override void HandleServerStatus(SyncMLStatus status)
        {
            string statusCode = status.Data.Content;
            Debug.WriteLine("Status " + statusCode + ": " + GetStatusReport(status));
            switch (status.CmdRef.Content)
            {
                case "0": //Handle header authentication
                    switch (statusCode)
                    {
                        case "212":
                            Facade.DisplayOperationMessage("Log on to SyncML server successfully.");
                            LoggedOn = true;
                            AuthenticationStatus = SyncMLAuthenticationStatus.LoggedOn;

                            MetaParser parser2 = new MetaParser(status.Chal.Meta.Xml);
                            MetaType metaType2 = parser2.GetMetaType();
                            if (metaType2 != null)
                            {
                                if (metaType2.Content == "syncml:auth-basic")
                                {
                                    AuthenticationStatus = SyncMLAuthenticationStatus.Base64Chal;
                                    Facade.AuthenticationTypeOfNextMessage = SyncMLAuthenticationType.Base64;
                                }
                                else if (metaType2.Content == "syncml:auth-md5")
                                {
                                    AuthenticationStatus = SyncMLAuthenticationStatus.MD5Chal;
                                    MetaNextNonce nextNonce = parser2.GetMetaNextNonce();
                                    Facade.Md5NextNonceFromServer = Convert.FromBase64String(nextNonce.Content);
                                    Facade.AuthenticationTypeOfNextMessage = SyncMLAuthenticationType.MD5;
                                }
                                else
                                    AuthenticationStatus = SyncMLAuthenticationStatus.UnknownChal;
                            }

                            break;
                        case "407":
                        case "401":
                            MetaParser parser = new MetaParser(status.Chal.Meta.Xml);
                            MetaType metaType = parser.GetMetaType();
                            if (metaType != null)
                            {
                                if (metaType.Content == "syncml:auth-basic")
                                    AuthenticationStatus = SyncMLAuthenticationStatus.Base64Chal;
                                else if (metaType.Content == "syncml:auth-md5")
                                {
                                    AuthenticationStatus = SyncMLAuthenticationStatus.MD5Chal;
                                    MetaNextNonce nextNonce = parser.GetMetaNextNonce();
                                    Facade.Md5NextNonceFromServer = Convert.FromBase64String(nextNonce.Content);
                                }
                                else
                                    AuthenticationStatus = SyncMLAuthenticationStatus.UnknownChal;
                            }
                            else
                                AuthenticationStatus = SyncMLAuthenticationStatus.FailedID;
                            Facade.DisplayOperationMessage("Authentication rejected. Please check user name and password.");
                            break;
                        default:
                            AuthenticationStatus = SyncMLAuthenticationStatus.UnknownStatus;
                            Facade.DisplayOperationMessage("The server sent back unexpected status code: " + statusCode);
                            Facade.SoFarOK = false;
                            break;
                    }

                    break;
                case "1": //server status to the sync alert command
                    switch (statusCode)
                    {
                        case "508":
                            Trace.TraceInformation("Server wants slown sync.");
                            //    Facade.ResponseCommandPool.Add(CreateSyncAlert(SyncType.Slow));
                            break;
                        case "200":
                            break;
                        default:
                            if (statusCode == "404")
                            {
                                Trace.TraceInformation("The server does not have requested data store.");
                                Facade.DisplayOperationMessage("The server does not have requested data store.");
                            }
                            Trace.TraceInformation(String.Format("Sync request is rejected. The server said: {0}", Facade.StatusMessages.GetMessage(statusCode)));
                            Facade.SoFarOK = false;
                            break;
                    }
                    break;
                case "2":// Handle response to previous Get
                    if (statusCode == "200")
                        Trace.TraceInformation("Received server device information successfully.");
                    else
                        Facade.SoFarOK = false;
                    break;
                case "3": // Handle response to previous Put.
                    if (statusCode == "200")
                        Trace.TraceInformation("The server received local device information successfully.");
                    else
                        Facade.SoFarOK = false;
                    break;
                default:
                    Trace.TraceInformation("Unexpected CmdRef in Status.");
                    Facade.SoFarOK = false;
                    break;

            }
        }

        /// <summary>
        /// Add Get command to a syncml message
        /// </summary>
        /// <param name="syncml"></param>
        /// <returns>The Get command.</returns>
        private static SyncMLGet AddGetDeviceInfo(SyncMLSyncML syncml)
        {
            SyncMLGet get = SyncMLGet.Create();
            get.CmdID = syncml.NextCmdID;
            get.Meta.Xml.Add(SyncMLSimpleElementFactory.Create<MetaType>("application/vnd.syncml-devinf+xml").Xml);
            SyncMLItem item = SyncMLItem.Create();
            item.Target.LocURI = SyncMLSimpleElementFactory.Create<SyncMLLocURI>("./devinf12"); //The Source element in the Item element MUST have a value ./devinf12.
            get.ItemCollection.Add(item);

            syncml.Body.Commands.Add(get);
            return get;
        }

        protected override bool ProcessResponse(string text)
        {
            if (!base.ProcessResponse(text))
                return false;

            CleanUp();
            return true;
        }

        /// <summary>
        /// Add a Put command to a syncml message
        /// </summary>
        /// <param name="syncml"></param>
        private SyncMLPut AddPutDeviceInfo(SyncMLSyncML syncml)
        {
            SyncMLPut put = SyncMLPut.Create();
            put.CmdID = syncml.NextCmdID;
            put.Meta.Xml.Add(SyncMLSimpleElementFactory.Create<MetaType>("application/vnd.syncml-devinf+xml").Xml);
            SyncMLItem item = SyncMLItem.Create();
            item.Source.LocURI = SyncMLSimpleElementFactory.Create<SyncMLLocURI>("./devinf12"); //The Source element in the Item element MUST have a value ./devinf12.
            item.Data.Xml.Add(Facade.LocalDevinf.Xml);
            put.ItemCollection.Add(item);

            syncml.Body.Commands.Add(put);
            return put;
        }

        private SyncMLAlert CreateSyncAlert(SyncType syncType)
        {
            //Tell the server what sync type
            SyncMLAlert alert = SyncMLAlert.Create();
            alert.CmdID = ClientSyncML.NextCmdID;

            MetaAnchor anchor;

            Facade.NextAnchor = DateTime.UtcNow.Ticks.ToString();

            switch (syncType)
            {
                case SyncType.TwoWay:
                    alert.Data.Content = "200";
                    anchor = MetaAnchor.Create(Facade.NextAnchor, Facade.LastAnchor);
                    Facade.DisplayOperationMessage("Last anchor: " + anchor.Last.Content);
                    break;
                case SyncType.Slow:
                    alert.Data.Content = "201";
                    //        anchor = MetaAnchor.Create(DateTime.Now.ToString("yyyyMMddTHHmmssZ"), "0");
                    anchor = MetaAnchor.Create(Facade.NextAnchor, Facade.LastAnchor);
                    break;
                case SyncType.OneWayFromClient:
                    alert.Data.Content = "202";
                    anchor = MetaAnchor.Create(Facade.NextAnchor, Facade.LastAnchor);
                    Facade.DisplayOperationMessage("Last anchor: " + anchor.Last.Content);
                    break;
                case SyncType.RefreshFromClient:
                    alert.Data.Content = "203";
                    anchor = MetaAnchor.Create(Facade.NextAnchor, Facade.LastAnchor);
                    Facade.DisplayOperationMessage("Last anchor: " + anchor.Last.Content);
                    break;
                case SyncType.OneWayFromServer:
                    alert.Data.Content = "204";
                    anchor = MetaAnchor.Create(Facade.NextAnchor, Facade.LastAnchor);
                    Facade.DisplayOperationMessage("Last anchor: " + anchor.Last.Content);
                    break;
                case SyncType.RefreshFromServer:
                    alert.Data.Content = "205";
                    anchor = MetaAnchor.Create(Facade.NextAnchor, Facade.LastAnchor);
                    Facade.DisplayOperationMessage("Last anchor: " + anchor.Last.Content);
                    break;
                default:
                    throw new FacadeErrorException("Unexpected sync type");
            }

            //Tell what to sync
            SyncMLItem item = SyncMLItem.Create();
            item.Target.LocURI.Content = Facade.ContactDataSourceAtServer;
            item.Source.LocURI.Content = Facade.LocalDataSource.DataSourceName;

            //Tell anchors
            item.Meta.Xml.Add(anchor.Xml);

            alert.ItemCollection.Add(item);

            return alert;
        }

        private SyncMLSyncML CreateSyncRequestMessage(SyncType syncType)
        {
            SyncMLAlert alert = CreateSyncAlert(syncType);

            ClientSyncML.Body.Commands.Add(alert);

            CommandAndStatusRegister.Add(alert);

            return ClientSyncML;
        }

    }

    /// <summary>
    /// This is used in one-way sync operations.
    /// </summary>
    internal class SendingEmptySyncMessage : SyncMLSyncMessageBase
    {
        public SendingEmptySyncMessage(SyncMLFacade facade)
            : base(facade)
        {

        }

        public override void Send()
        {
            SyncMLSyncML syncml = CreateSyncContentMessage();
            syncml.Hdr.Target.LocURI.Content = Facade.BasicUriText; //Facade.CurrentRespUri;

            syncml.Body.MarkFinal();

            Debug.WriteLine("Sending EmptySync:" + syncml.Xml.ToString());
            string responseText = Facade.Connections.GetResponseText(syncml.Xml.ToString(SaveOptions.DisableFormatting));
            if (String.IsNullOrEmpty(responseText))
                return;
            Facade.CurrentMsgID++;
            ProcessResponse(responseText);
        }

        /// <summary>
        /// Load sync content from the sync command buffer in Fcade.CommandsToSend.
        /// </summary>
        /// <param name="syncML"></param>
        protected void LoadSyncContent(SyncMLSyncML syncML)
        {
            SyncMLSync syncContent = SyncMLSync.Create();
            syncContent.CmdID = syncML.NextCmdID;
            syncContent.Source.LocURI.Content = Facade.LocalDataSource.DataSourceName;
            syncContent.Target.LocURI.Content = Facade.ContactDataSourceAtServer;

            syncML.Body.Commands.Add(syncContent);
            CommandAndStatusRegister.Add(syncContent);
        }

        private SyncMLSyncML CreateSyncContentMessage()
        {
            MoveCommandsInPoolToMessage(ClientSyncML);

            LoadSyncContent(ClientSyncML);

            return ClientSyncML;
        }

        protected override void HandleServerStatus(SyncMLStatus status)
        {
            string statusCode = status.Data.Content;
            Debug.WriteLine("Status " + statusCode + ": " + GetStatusReport(status));

            switch (statusCode)
            {
                case "207":  //The user updated the same record on more than 1 devices
                    if (status.SourceRefCollection.Count > 0)
                    {
                        string localID = status.SourceRefCollection[0].Content;
                        string contactName = Facade.LocalDataSource.GetItemName(localID);
                        Facade.DisplayOperationMessage(String.Format("When updating for {0}, the sync server found conflicts. Though the conflicts were resolved by the sync server through data merge, please manually check to ensure everything is OK.", contactName));
                    }
                    break;
                default:
                    break;
            }
        }

        protected override bool ProcessResponse(string text)
        {
            if (!base.ProcessResponse(text))
                return false;

            CleanUp();
            return true;
        }

    }


    /// <summary>
    /// Process quick sync message.
    /// </summary>
    internal class SendingSyncMessage : SyncMLSyncMessageBase
    {
        /// <summary>
        /// The sync command will contain NumberOfChanges. This is generally used for sending the first message with Sync command.
        /// </summary>
        /// <param name="facade"></param>
        /// <param name="numberOfChanges"></param>
        public SendingSyncMessage(SyncMLFacade facade, int numberOfChanges)
            : base(facade)
        {
            this.numberOfChanges = numberOfChanges;
        }

        /// <summary>
        /// This is used for sending the second and next messages with Sync command.
        /// </summary>
        /// <param name="facade"></param>
        public SendingSyncMessage(SyncMLFacade facade)
            : base(facade)
        {
        }

        private int numberOfChanges;

        public override void Send()
        {
            SyncMLSyncML syncml = CreateSyncContentMessage();
            syncml.Hdr.Target.LocURI.Content = Facade.BasicUriText;// Facade.CurrentRespUri;

            if (!Facade.MoreSyncCommands)
            {
                syncml.Body.MarkFinal();
            }

            Debug.WriteLine("Sending SyncMessage:" + syncml.Xml.ToString());
            string responseText = Facade.Connections.GetResponseText(syncml.Xml.ToString(SaveOptions.DisableFormatting));
            if (String.IsNullOrEmpty(responseText))
                return;

            Facade.CurrentMsgID++;
            ProcessResponse(responseText);
        }

        /// <summary>
        /// Called by ProcessResponseForXXX in order to respond to Status returned by server
        /// </summary>
        /// <param name="status">SyncMLStatus object which is part of syncml.</param>
        protected override void HandleServerStatus(SyncMLStatus status)
        {
            string statusCode = status.Data.Content;

            switch (statusCode)
            {
                case "207":  //The user updated the same record on more than 1 devices
                    if (status.SourceRefCollection.Count > 0)
                    {
                        string localID = status.SourceRefCollection[0].Content;
                        string contactName = Facade.LocalDataSource.GetItemName(localID);
                        string m = String.Format("When updating for {0}, the sync server found conflicts. Though the conflicts were resolved by the sync server through data merge, please manually check to ensure everything is OK.", contactName);
                        Facade.DisplayOperationMessage(m);
                    }
                    break;
                case "402":
                    Facade.DisplayOperationMessage("The server requires payment.");
                    Facade.SoFarOK = false;
                    break;
                case "511": // this is with the status for Hdr
                    Facade.DisplayOperationMessage("Serious error with SyncHdr.");
                    Facade.SoFarOK = false;
                    break;
                default:
                    break;
            }
            Debug.WriteLine("Status " + statusCode + ": " + GetStatusReport(status));
        }

        private int numberOfCommandsToSend;
        public int NumberOfCommandsToSend
        {
            get { return numberOfCommandsToSend; }
        }


        /// <summary>
        /// Load sync content from the sync command buffer in Fcade.CommandsToSend.
        /// </summary>
        /// <param name="syncML"></param>
        private void LoadSyncContent(SyncMLSyncML syncML)
        {
            SyncMLSync syncContent = SyncMLSync.Create();
            syncContent.CmdID = syncML.NextCmdID;
            syncContent.Source.LocURI.Content = Facade.LocalDataSource.DataSourceName;
            syncContent.Target.LocURI.Content = Facade.ContactDataSourceAtServer;

            if (numberOfChanges > 0)
            {
                syncContent.NumberOfChanges.Content = numberOfChanges.ToString();
            }

            Facade.ExtractNextCommands(syncContent.Commands);

            foreach (SyncMLCommand command in syncContent.Commands)
            {
                command.CmdID = syncML.NextCmdID;  //commands from XmlToSyncMLSyncCommands does not contain CmdID
                CommandAndStatusRegister.Add(command);
            }

            syncML.Body.Commands.Add(syncContent);
            CommandAndStatusRegister.Add(syncContent);
            numberOfCommandsToSend = syncContent.Commands.Count;
        }



        /// <summary>
        /// Create Sync request XML from the command buffer extacting a number of commands. 
        /// The message will also include responses to previous server responses.
        /// </summary>
        /// <returns></returns>
        private SyncMLSyncML CreateSyncContentMessage()
        {
            LoadSyncContent(ClientSyncML);

            MoveCommandsInPoolToMessage(ClientSyncML);

            return ClientSyncML;
        }

        protected override bool ProcessResponse(string text)
        {
            if (!base.ProcessResponse(text))
                return false;

            int numberOfCommandsSent = NumberOfCommandsToSend;
            Facade.IncrementProgressBar(NumberOfCommandsToSend);

            //3: Send the rest of sync commands
            if (Facade.MoreSyncCommands)
            {
                SendingSyncMessage sendingSyncStep = new SendingSyncMessage(Facade);
                sendingSyncStep.Send();

                if (!Facade.SoFarOK)
                {
                    Trace.TraceInformation("Somethng wrong in message returned from the server.");
                    return false;
                }

                numberOfCommandsSent += sendingSyncStep.NumberOfCommandsToSend;

                if (Facade.GracefulStop)
                    return false;

            }
            else
            {
                Facade.DisplayStageMessage("Sending Done");
                CleanUp();
            }

            return true;
        }

    }

}

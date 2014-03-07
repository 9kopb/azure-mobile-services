﻿// ----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ----------------------------------------------------------------------------

namespace Microsoft.WindowsAzure.MobileServices
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.IO.IsolatedStorage;

    /// <summary>
    /// The value will be stored in the following keys:
    ///     Version: the storage version
    ///     ChannelUri: the latest channelUri used for creation.
    ///     Registrations: {registartionName1}:{registartionId1};{registartionName2}:{registartionId2}
    ///  
    /// When create/delete is called, channelUri will be udpate.
    /// When create/update/get/delete registations, registartions value will be updated.
    /// </summary>
    internal class LocalStorageManager : IDisposable
    {
        internal const string StorageVersion = "v1.0.0";

        internal const string PrimaryChannelId = "$Primary";

        private readonly object flushLock = new object();

        private string channelUri;

        private readonly string keyNameForVersion;

        private readonly string keyNameForChannelUri;

        private readonly string keyNameForRegistrations;

        private ConcurrentDictionary<string, StoredRegistrationEntry> registrations;

        private readonly IDictionary<string, object> storageValues;

        public LocalStorageManager(string applicationUri)
        {
            this.keyNameForVersion = string.Format("{0}-Version", applicationUri);
            this.keyNameForChannelUri = string.Format("{0}-Channel", applicationUri);
            this.keyNameForRegistrations = string.Format("{0}-Registrations", applicationUri);

            this.storageValues = IsolatedStorageSettings.ApplicationSettings;

            this.InitializeRegistrationInfoFromStorage();
        }

        public bool IsRefreshNeeded { get; internal set; }

        public string ChannelUri
        {
            get
            {
                return this.channelUri;
            }
            set
            {
                if (this.channelUri == null || !this.channelUri.Equals(value))
                {
                    this.channelUri = value;
                    this.Flush();
                }
            }
        }

        public StoredRegistrationEntry GetRegistration(string registrationName)
        {
            StoredRegistrationEntry reg;
            if (this.registrations.TryGetValue(registrationName, out reg))
            {
                return reg;
            }

            return null;
        }

        public void DeleteRegistrationByName(string registrationName)
        {
            if (this.registrations.Remove(registrationName))
            {
                this.Flush();
            }
        }

        public void DeleteRegistrationByRegistrationId(string registrationId)
        {
            var found = registrations.FirstOrDefault(v => v.Value.RegistrationId.Equals(registrationId));
            if (!found.Equals(default(KeyValuePair<string, StoredRegistrationEntry>)))
            {
                this.DeleteRegistrationByName(found.Key);
            }
        }

        public void UpdateRegistrationByName(string registrationName, string registrationId, string registrationChannelUri)
        {
            StoredRegistrationEntry cacheReg = new StoredRegistrationEntry(registrationName, registrationId);
            this.registrations.AddOrUpdate(registrationName, cacheReg, (key, oldValue) => cacheReg);

            this.channelUri = registrationChannelUri;
            this.Flush();
        }

        public void UpdateRegistrationByRegistrationId(string registrationId, string registrationName, string registrationChannelUri)
        {
            // update registation is registartionId is in cached registartions, otherwise create new one
            var found = registrations.FirstOrDefault(v => v.Value.RegistrationId.Equals(registrationId));
            if (!found.Equals(default(KeyValuePair<string, StoredRegistrationEntry>)))
            {
                this.UpdateRegistrationByName(found.Key, found.Value.RegistrationId, registrationChannelUri);
            }
            else
            {
                this.UpdateRegistrationByName(registrationName, registrationId, registrationChannelUri);
            }
        }

        public void ClearRegistrations()
        {
            this.registrations.Clear();
            Flush();
        }

        public void RefreshFinished(string refreshedChannelUri)
        {
            this.ChannelUri = refreshedChannelUri;
            this.IsRefreshNeeded = false;
        }

        private static string ReadContent(IDictionary<string, object> values, string key)
        {
            if (values.ContainsKey(key))
            {
                return values[key] as string;
            }

            return string.Empty;
        }

        private static void SetContent(IDictionary<string, object> values, string key, string value)
        {
            if (values.ContainsKey(key))
            {
                values[key] = value;
            }
            else
            {
                values.Add(key, value);
            }
        }

        private void Flush()
        {
            lock (this.flushLock)
            {
                SetContent(this.storageValues, this.keyNameForVersion, StorageVersion);
                SetContent(this.storageValues, this.keyNameForChannelUri, this.channelUri);

                string str = string.Empty;
                if (this.registrations != null)
                {
                    var entries = this.registrations.Select(v => v.Value.ToString());
                    str = string.Join(";", entries);
                }

                SetContent(this.storageValues, this.keyNameForRegistrations, str);
                IsolatedStorageSettings.ApplicationSettings.Save();
            }
        }

        private void InitializeRegistrationInfoFromStorage()
        {
            this.registrations = new ConcurrentDictionary<string, StoredRegistrationEntry>();

            // Read channelUri
            this.channelUri = ReadContent(this.storageValues, this.keyNameForChannelUri);

            // Verify storage version
            var version = ReadContent(this.storageValues, this.keyNameForVersion);
            if (!string.Equals(version, StorageVersion, System.StringComparison.OrdinalIgnoreCase))
            {
                this.IsRefreshNeeded = true;
                return;
            }

            this.IsRefreshNeeded = false;

            // read registrations
            var regsStr = ReadContent(this.storageValues, this.keyNameForRegistrations);
            if (!string.IsNullOrEmpty(regsStr))
            {
                var entries = regsStr.Split(';');
                foreach (string entry in entries)
                {
                    var reg = StoredRegistrationEntry.CreateFromString(entry);
                    this.registrations.AddOrUpdate(reg.RegistrationName, reg, (key, oldValue) => reg);
                }
            }
        }

        public void Dispose()
        {
            if (this.registrations != null)
            {
                this.registrations.Dispose();
                this.registrations = null;
            }
        }
    }
}

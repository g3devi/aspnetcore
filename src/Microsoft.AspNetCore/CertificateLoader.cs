// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace Microsoft.AspNetCore
{
    /// <summary>
    /// A helper class to load certificates from files and certificate stores based on <seealso cref="IConfiguration"/> data.
    /// </summary>
    public class CertificateLoader
    {
        private readonly IConfiguration _certificatesConfiguration;
        private readonly ICertificateFileLoader _certificateFileLoader;
        private readonly ICertificateStoreLoader _certificateStoreLoader;

        /// <summary>
        /// Creates a new instance of <see cref="CertificateLoader"/>.
        /// </summary>
        public CertificateLoader()
            : this(null)
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="CertificateLoader"/> that can load certificate references from configuration.
        /// </summary>
        /// <param name="certificatesConfiguration">An <see cref="IConfiguration"/> with information about certificates.</param>
        public CertificateLoader(IConfiguration certificatesConfiguration)
            : this(certificatesConfiguration, new CertificateFileLoader(), new CertificateStoreLoader())
        {
            _certificatesConfiguration = certificatesConfiguration;
        }

        internal CertificateLoader(IConfiguration certificatesConfiguration, ICertificateFileLoader certificateFileLoader, ICertificateStoreLoader certificateStoreLoader)
        {
            _certificatesConfiguration = certificatesConfiguration;
            _certificateFileLoader = certificateFileLoader;
            _certificateStoreLoader = certificateStoreLoader;
        }

        /// <summary>
        /// Loads one or more certificates based on the information found in a configuration section.
        /// </summary>
        /// <param name="certificateConfiguration">A configuration section containing either a string value referencing certificates
        /// by name, or one or more inline certificate specifications.
        /// </param>
        /// <returns>One or more loaded certificates.</returns>
        public IEnumerable<X509Certificate2> Load(IConfigurationSection certificateConfiguration)
        {
            var certificateNames = certificateConfiguration.Value;
            var certificates = new List<X509Certificate2>();

            if (certificateNames != null)
            {
                foreach (var certificateName in certificateNames.Split(';'))
                {
                    var certificate = LoadSingle(certificateName);
                    if (certificate != null)
                    {
                        certificates.Add(certificate);
                    }
                }
            }
            else
            {
                if (certificateConfiguration["Source"] != null)
                {
                    var certificate = LoadSingle(certificateConfiguration);
                    if (certificate != null)
                    {
                        certificates.Add(certificate);
                    }
                }
                else
                {
                    certificates.AddRange(LoadMultiple(certificateConfiguration));
                }
            }

            return certificates;
        }

        /// <summary>
        /// Loads a certificate by name.
        /// </summary>
        /// <param name="certificateName">The certificate name.</param>
        /// <returns>The loaded certificate</returns>
        /// <remarks>This method only works if the <see cref="CertificateLoader"/> instance was constructed with
        /// a reference to an <see cref="IConfiguration"/> instance containing named certificates.
        /// </remarks>
        private X509Certificate2 LoadSingle(string certificateName)
        {
            var certificateConfiguration = _certificatesConfiguration?.GetSection(certificateName);

            if (!certificateConfiguration.Exists())
            {
                throw new InvalidOperationException($"No certificate named {certificateName} found in configuration");
            }

            return LoadSingle(certificateConfiguration);
        }

        private X509Certificate2 LoadSingle(IConfigurationSection certificateConfiguration)
        {
            var sourceKind = certificateConfiguration["Source"];

            CertificateSource certificateSource;
            switch (sourceKind.ToLowerInvariant())
            {
                case "file":
                    certificateSource = new CertificateFileSource(_certificateFileLoader);
                    break;
                case "store":
                    certificateSource = new CertificateStoreSource(_certificateStoreLoader);
                    break;
                default:
                    throw new InvalidOperationException($"Invalid certificate source kind: {sourceKind}");
            }

            certificateConfiguration.Bind(certificateSource);

            return certificateSource.Load();
        }

        private IEnumerable<X509Certificate2> LoadMultiple(IConfigurationSection certificatesConfiguration)
            => certificatesConfiguration.GetChildren()
                .Select(LoadSingle)
                .Where(c => c != null);

        private abstract class CertificateSource
        {
            public string Source { get; set; }

            public abstract X509Certificate2 Load();
        }

        private class CertificateFileSource : CertificateSource
        {
            private ICertificateFileLoader _certificateFileLoader;

            public CertificateFileSource(ICertificateFileLoader certificateFileLoader)
            {
                _certificateFileLoader = certificateFileLoader;
            }

            public string Path { get; set; }

            public string Password { get; set; }

            public override X509Certificate2 Load()
            {
                var certificate = TryLoad(X509KeyStorageFlags.DefaultKeySet, out var error)
                    ?? TryLoad(X509KeyStorageFlags.UserKeySet, out error)
#if NETCOREAPP2_0
                    ?? TryLoad(X509KeyStorageFlags.EphemeralKeySet, out error)
#endif
                    ;

                if (error != null)
                {
                    throw error;
                }

                return certificate;
            }

            private X509Certificate2 TryLoad(X509KeyStorageFlags flags, out Exception exception)
            {
                try
                {
                    var loadedCertificate = _certificateFileLoader.Load(Path, Password, flags);
                    exception = null;
                    return loadedCertificate;
                }
                catch (Exception e)
                {
                    exception = e;
                    return null;
                }
            }
        }

        private class CertificateStoreSource : CertificateSource
        {
            private readonly ICertificateStoreLoader _certificateStoreLoader;

            public CertificateStoreSource(ICertificateStoreLoader certificateStoreLoader)
            {
                _certificateStoreLoader = certificateStoreLoader;
            }

            public string Subject { get; set; }
            public string StoreName { get; set; }
            public string StoreLocation { get; set; }
            public bool AllowInvalid { get; set; }

            public override X509Certificate2 Load()
            {
                if (!Enum.TryParse(StoreLocation, ignoreCase: true, result: out StoreLocation storeLocation))
                {
                    throw new InvalidOperationException($"Invalid store location: {StoreLocation}");
                }

                return _certificateStoreLoader.Load(Subject, StoreName, storeLocation, !AllowInvalid);
            }
        }
    }
}

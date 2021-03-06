﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ImageCacheBase.cs" company="James Jackson-South">
//   Copyright (c) James Jackson-South.
//   Licensed under the Apache License, Version 2.0.
// </copyright>
// <summary>
//   The image cache base provides methods for implementing the <see cref="IImageCache" /> interface.
//   It is recommended that any implementations inherit from this class.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace ImageProcessor.Web.Caching
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Threading.Tasks;
    using System.Web;

    using ImageProcessor.Web.Configuration;
    using ImageProcessor.Web.Extensions;
    using ImageProcessor.Web.Helpers;

    /// <summary>
    /// The image cache base provides methods for implementing the <see cref="IImageCache"/> interface.
    /// It is recommended that any implementations inherit from this class.
    /// </summary>
    public abstract class ImageCacheBase : IImageCache
    {
        /// <summary>
        /// The request path for the image.
        /// </summary>
        protected readonly string RequestPath;

        /// <summary>
        /// The full path for the image.
        /// </summary>
        protected readonly string FullPath;

        /// <summary>
        /// The querystring containing processing instructions.
        /// </summary>
        protected readonly string Querystring;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageCacheBase"/> class.
        /// </summary>
        /// <param name="requestPath">
        /// The request path for the image.
        /// </param>
        /// <param name="fullPath">
        /// The full path for the image.
        /// </param>
        /// <param name="querystring">
        /// The querystring containing instructions.
        /// </param>
        protected ImageCacheBase(string requestPath, string fullPath, string querystring)
        {
            this.RequestPath = requestPath;
            this.FullPath = fullPath;
            this.Querystring = querystring;
            this.Settings = this.AugmentSettingsCore(ImageProcessorConfiguration.Instance.ImageCacheSettings);
            this.MaxDays = ImageProcessorConfiguration.Instance.ImageCacheMaxDays;
            this.BrowserMaxDays = ImageProcessorConfiguration.Instance.BrowserCacheMaxDays;
        }

        /// <summary>
        /// Gets or sets any additional settings required by the cache.
        /// </summary>
        public Dictionary<string, string> Settings { get; set; }

        /// <summary>
        /// Gets or sets the path to the cached image.
        /// </summary>
        public string CachedPath { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of days to store the image.
        /// </summary>
        public int MaxDays { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of days to cache the image in the browser.
        /// </summary>
        public int BrowserMaxDays { get; set; }

        /// <summary>
        /// Gets a value indicating whether the image is new or updated in an asynchronous manner.
        /// </summary>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public abstract Task<bool> IsNewOrUpdatedAsync();

        /// <summary>
        /// Adds the image to the cache in an asynchronous manner.
        /// </summary>
        /// <param name="stream">
        /// The stream containing the image data.
        /// </param>
        /// <param name="contentType">
        /// The content type of the image.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/> representing an asynchronous operation.
        /// </returns>
        public abstract Task AddImageToCacheAsync(Stream stream, string contentType);

        /// <summary>
        /// Trims the cache of any expired items in an asynchronous manner.
        /// </summary>
        /// <returns>
        /// The asynchronous <see cref="Task"/> representing an asynchronous operation.
        /// </returns>
        public abstract Task TrimCacheAsync();

        /// <summary>
        /// Gets a string identifying the cached file name.
        /// </summary>
        /// <returns>
        /// The asynchronous <see cref="Task"/> returning the value.
        /// </returns>
        public virtual async Task<string> CreateCachedFileNameAsync()
        {
            string streamHash = string.Empty;

            try
            {
                if (new Uri(this.RequestPath).IsFile)
                {
                    // Get the hash for the filestream. That way we can ensure that if the image is
                    // updated but has the same name we will know.
                    FileInfo imageFileInfo = new FileInfo(this.RequestPath);
                    if (imageFileInfo.Exists)
                    {
                        // Pull the latest info.
                        imageFileInfo.Refresh();

                        // Checking the stream itself is far too processor intensive so we make a best guess.
                        string creation = imageFileInfo.CreationTimeUtc.ToString(CultureInfo.InvariantCulture);
                        string length = imageFileInfo.Length.ToString(CultureInfo.InvariantCulture);
                        streamHash = $"{creation}{length}";
                    }
                }
                else
                {
                    // Try and get the headers for the file, this should allow cache busting for remote files.
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(this.RequestPath);
                    request.Method = "HEAD";

                    using (HttpWebResponse response = (HttpWebResponse)(await request.GetResponseAsync()))
                    {
                        string lastModified = response.LastModified.ToUniversalTime().ToString(CultureInfo.InvariantCulture);
                        string length = response.ContentLength.ToString(CultureInfo.InvariantCulture);
                        streamHash = $"{lastModified}{length}";
                    }
                }
            }
            catch
            {
                streamHash = string.Empty;
            }

            // Use an sha1 hash of the full path including the querystring to create the image name.
            // That name can also be used as a key for the cached image and we should be able to use
            // The characters of that hash as sub-folders.
            string parsedExtension = ImageHelpers.Instance.GetExtension(this.FullPath, this.Querystring);
            string encryptedName = (streamHash + this.FullPath).ToSHA1Fingerprint();

            string cachedFileName = $"{encryptedName}.{(!string.IsNullOrWhiteSpace(parsedExtension) ? parsedExtension.Replace(".", string.Empty) : "jpg")}";

            return await Task.FromResult(cachedFileName);
        }

        /// <summary>
        /// Rewrites the path to point to the cached image.
        /// </summary>
        /// <param name="context">
        /// The <see cref="HttpContext"/> encapsulating all information about the request.
        /// </param>
        public abstract void RewritePath(HttpContext context);

        /// <summary>
        /// Gets a value indicating whether the given images creation date is out with
        /// the prescribed limit.
        /// </summary>
        /// <param name="creationDate">
        /// The creation date.
        /// </param>
        /// <returns>
        /// The true if the date is out with the limit, otherwise; false.
        /// </returns>
        protected virtual bool IsExpired(DateTime creationDate)
        {
            return creationDate < DateTime.UtcNow.AddDays(-this.MaxDays);
        }

        /// <summary>
        /// Provides a means to augment the cache settings taken from the configuration in derived classes. 
        /// This allows for configuration of cache objects outside the normal configuration files, for example
        /// by using app settings in the Azure platform.
        /// </summary>
        /// <param name="settings">The current settings.</param>
        protected virtual void AugmentSettings(Dictionary<string, string> settings)
        {
        }

        /// <summary>
        /// Provides an entry point to augmentation of the <see cref="Settings"/> dictionary
        /// </summary>
        /// <param name="settings">Dictionary of settings</param>
        /// <returns>augmented dictionary of settings</returns>
        private Dictionary<string, string> AugmentSettingsCore(Dictionary<string, string> settings)
        {
            this.AugmentSettings(settings);
            return settings;
        }
    }
}

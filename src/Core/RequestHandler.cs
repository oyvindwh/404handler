﻿using System;
using System.Diagnostics;
using System.Net;
using System.Web;
using BVNetwork.NotFound.Core.Configuration;
using BVNetwork.NotFound.Core.CustomRedirects;
using BVNetwork.NotFound.Core.Data;
using BVNetwork.NotFound.Core.Logging;
using EPiServer.Logging;

namespace BVNetwork.NotFound.Core
{
    public class RequestHandler
    {
        private readonly IRedirectHandler _redirectHandler;
        private readonly IRequestLogger _requestLogger;
        private readonly IConfiguration _configuration;

        private static readonly ILogger Logger = LogManager.GetLogger();

        public RequestHandler(
            IRedirectHandler redirectHandler,
            IRequestLogger requestLogger,
            IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _requestLogger = requestLogger ?? throw new ArgumentNullException(nameof(requestLogger));
            _redirectHandler = redirectHandler ?? throw new ArgumentNullException(nameof(redirectHandler));
        }

        public virtual void Handle(HttpContextBase context)
        {
            if (context?.Response.StatusCode != 404) return;

            // If we're only doing this for remote users, we need to test for local host
            if (_configuration.FileNotFoundHandlerMode == FileNotFoundMode.RemoteOnly)
            {
                // Determine if we're on localhost
                var localHost = IsLocalhost(context);
                if (localHost)
                {
                    Logger.Debug("Determined to be localhost, returning");
                    return;
                }
                Logger.Debug("Not localhost, handling error");
            }

            Logger.Debug("FileNotFoundHandler called");

            var notFoundUri = context.Request.Url;

            if (IsResourceFile(notFoundUri)) return;

            var query = context.Request.ServerVariables["QUERY_STRING"];

            // avoid duplicate log entries
            if (query != null && query.StartsWith("404;"))
            {
                return;
            }

            var canHandleRedirect = HandleRequest(context.Request.UrlReferrer, notFoundUri, out var newUrl);
            if (canHandleRedirect && newUrl.State == (int)DataStoreHandler.State.Saved)
            {
                context.Response.Clear();
                context.Response.TrySkipIisCustomErrors = true;
                context.Server.ClearError();
                context.Response.RedirectPermanent(newUrl.NewUrl);
                context.Response.End();
            }
            else if (canHandleRedirect && newUrl.State == (int)DataStoreHandler.State.Deleted)
            {
                SetStatusCodeAndShow404(context, 410);
            }
            else
            {
                SetStatusCodeAndShow404(context);
            }
        }

        public virtual bool HandleRequest(Uri referrer, Uri urlNotFound, out CustomRedirect foundRedirect)
        {
            var redirect = _redirectHandler.Find(urlNotFound, referrer);

            foundRedirect = null;
            var pathAndQuery = urlNotFound.PathAndQuery;

            if (redirect != null)
            {
                // Url has been deleted from this site
                if (redirect.State.Equals((int)DataStoreHandler.State.Deleted))
                {
                    foundRedirect = redirect;
                    return true;
                }

                if (redirect.State.Equals((int)DataStoreHandler.State.Saved))
                {
                    // Found it, however, we need to make sure we're not running in an
                    // infinite loop. The new url must not be the referrer to this page
                    if (string.Compare(redirect.NewUrl, pathAndQuery, StringComparison.InvariantCultureIgnoreCase) != 0)
                    {

                        foundRedirect = redirect;
                        return true;
                    }
                }
            }
            else
            {
                // log request to database - if logging is turned on.
                if (_configuration.Logging == LoggerMode.On)
                {
                    // Safe logging
                    _requestLogger.LogRequest(pathAndQuery, referrer?.ToString());
                }
            }
            return false;
        }

        public virtual void SetStatusCodeAndShow404(HttpContextBase context, int statusCode = 404)
        {
            context.Response.TrySkipIisCustomErrors = true;
            context.Server.ClearError();
            context.Response.StatusCode = statusCode;
            context.Response.End();
        }

        /// <summary>
        /// Determines whether the specified not found URI is a resource file
        /// </summary>
        /// <param name="notFoundUri">The not found URI.</param>
        /// <returns>
        /// <c>true</c> if it is a resource file; otherwise, <c>false</c>.
        /// </returns>
        public virtual bool IsResourceFile(Uri notFoundUri)
        {
            var extension = notFoundUri.AbsolutePath;
            var extPos = extension.LastIndexOf('.');

            if (extPos <= 0) return false;

            extension = extension.Substring(extPos + 1);
            if (_configuration.IgnoredResourceExtensions.Contains(extension))
            {
                // Ignoring 404 rewrite of known resource extension
                Logger.Debug("Ignoring rewrite of '{0}'. '{1}' is a known resource extension", notFoundUri.ToString(), extension);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Determines whether the current request is on localhost.
        /// </summary>
        /// <returns>
        /// <c>true</c> if current request is localhost; otherwise, <c>false</c>.
        /// </returns>
        public virtual bool IsLocalhost(HttpContextBase context)
        {
            try
            {
                var hostAddress = context.Request.UserHostAddress ?? string.Empty;
                var address = IPAddress.Parse(hostAddress);
                Debug.WriteLine("IP Address of user: " + address, "404Handler");

                var host = Dns.GetHostEntry(Dns.GetHostName());
                Debug.WriteLine("Host Entry of local computer: " + host.HostName, "404Handler");
                return address.Equals(IPAddress.Loopback) || Array.IndexOf(host.AddressList, address) >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
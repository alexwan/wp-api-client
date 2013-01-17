﻿// -----------------------------------------------------------------------
// <copyright file="ApiRequestHandler.cs" company="Nokia">
// Copyright (c) 2012, Nokia
// All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using Newtonsoft.Json.Linq;

namespace Nokia.Music.Phone.Internal
{
    /// <summary>
    /// Implementation of the raw API interface for making requests
    /// </summary>
    internal class ApiRequestHandler : IApiRequestHandler
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApiRequestHandler" /> class.
        /// </summary>
        /// <param name="uriBuilder">The URI builder.</param>
        public ApiRequestHandler(IApiUriBuilder uriBuilder)
        {
            this.UriBuilder = uriBuilder;
        }

        /// <summary>
        /// Gets the URI builder that is being used.
        /// </summary>
        /// <value>
        /// The URI builder.
        /// </value>
        public IApiUriBuilder UriBuilder { get; private set; }

        /// <summary>
        /// Makes the API request
        /// </summary>
        /// <param name="method">The method to call.</param>
        /// <param name="settings">The music client settings.</param>
        /// <param name="pathParams">The path params.</param>
        /// <param name="querystringParams">The querystring params.</param>
        /// <param name="callback">The callback to hit when done.</param>
        /// <param name="requestHeaders">HTTP headers to add to the request</param>
        /// <exception cref="System.ArgumentNullException">Thrown when no callback is specified</exception>
        public void SendRequestAsync(
                                     ApiMethod method,
                                     IMusicClientSettings settings,
                                     Dictionary<string, string> pathParams,
                                     Dictionary<string, string> querystringParams,
                                     Action<Response<JObject>> callback,
                                     Dictionary<string, string> requestHeaders = null)
        {
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }

            Uri uri = this.UriBuilder.BuildUri(method, settings, pathParams, querystringParams);

            Debug.WriteLine("Calling " + uri.ToString());

            TimedRequest request = new TimedRequest(uri);
            this.AddRequestHeaders(request.WebRequest, requestHeaders);
            request.BeginGetResponse(
                (IAsyncResult ar) =>
                    {
                    if (request.HasTimedOut)
                    {
                        return;
                    }

                    request.Dispose();
                    WebResponse response = null;
                    HttpWebResponse webResponse = null;
                    JObject json = null;
                    HttpStatusCode? statusCode = null;
                    Exception error = null;

                    try
                    {
                        response = request.WebRequest.EndGetResponse(ar);
                        webResponse = response as HttpWebResponse;
                        if (webResponse != null)
                        {
                            statusCode = webResponse.StatusCode;
                        }
                    }
                    catch (WebException ex)
                    {
                        error = ex;
                        if (ex.Response != null)
                        {
                            webResponse = (HttpWebResponse)ex.Response;
                            statusCode = webResponse.StatusCode;
                        }
                    }

                    string contentType = null;
                    string result = null;

                    if (response != null)
                    {
                        contentType = response.ContentType;
                        using (Stream responseStream = response.GetResponseStream())
                        {
                            result = responseStream.AsString();
                            if (!string.IsNullOrEmpty(result))
                            {
                                try
                                {
                                    json = JObject.Parse(result);
                                }
                                catch (Exception ex)
                                {
                                    error = ex;
                                    json = null;
                                }
                            }
                        }
                    }

                    if (json != null)
                    {
                        callback(new Response<JObject>(statusCode, contentType, json, method.RequestId));
                    }
                    else
                    {
                        callback(new Response<JObject>(statusCode, error, method.RequestId));
                    }
                },
                () => callback(new Response<JObject>(null, new ApiCallFailedException(), method.RequestId)),
                request);
        }

        private void AddRequestHeaders(WebRequest request, Dictionary<string, string> requestHeaders)
        {
            if (requestHeaders != null)
            {
                foreach (KeyValuePair<string, string> header in requestHeaders)
                {
                    request.Headers[header.Key] = header.Value;
                }
            }
        }
    }
}
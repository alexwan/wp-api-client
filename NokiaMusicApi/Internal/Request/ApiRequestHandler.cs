﻿// -----------------------------------------------------------------------
// <copyright file="ApiRequestHandler.cs" company="Nokia">
// Copyright (c) 2013, Nokia
// All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Nokia.Music.Commands;
using Nokia.Music.Internal.Compression;
using Nokia.Music.Internal.Response;

namespace Nokia.Music.Internal.Request
{
    /// <summary>
    /// Implementation of the raw API interface for making requests
    /// </summary>
#if OPEN_INTERNALS
        public
#else
        internal
#endif
    class ApiRequestHandler : IApiRequestHandler
    {
        private TimeSpan _serverTimeOffset = new TimeSpan(0);
        private bool _obtainedServerTime = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiRequestHandler" /> class.
        /// </summary>
        /// <param name="uriBuilder">The URI builder.</param>
        /// <param name="gzipHandler">The gzip handler</param>
        public ApiRequestHandler(IApiUriBuilder uriBuilder, IGzipHandler gzipHandler)
        {
            this.UriBuilder = uriBuilder;
            this.GzipHandler = gzipHandler;
        }

        /// <summary>
        /// Gets the URI builder that is being used.
        /// </summary>
        /// <value>
        /// The URI builder.
        /// </value>
        public IApiUriBuilder UriBuilder { get; private set; }

        /// <summary>
        /// Gets the server UTC time.
        /// </summary>
        /// <value>
        /// The server UTC time.
        /// </value>
        public DateTime ServerTimeUtc
        {
            get
            {
                return DateTime.UtcNow.Add(this._serverTimeOffset);
            }
        }

        /// <summary>
        /// Gets the handler gzip logic
        /// </summary>
        protected IGzipHandler GzipHandler { get; private set; }

        /// <summary>
        /// Makes the API request
        /// </summary>
        /// <typeparam name="T">The type of response item</typeparam>
        /// <param name="command">The command to call.</param>
        /// <param name="settings">The music client settings.</param>
        /// <param name="queryParams">The querystring.</param>
        /// <param name="callback">The callback to hit when done.</param>
        /// <param name="requestHeaders">HTTP headers to add to the request</param>
        /// <exception cref="System.ArgumentNullException">Thrown when no callback is specified</exception>
        public virtual void SendRequestAsync<T>(
                                     MusicClientCommand command,
                                     IMusicClientSettings settings,
                                     List<KeyValuePair<string, string>> queryParams,
                                     IResponseCallback<T> callback,
                                     Dictionary<string, string> requestHeaders = null)
        {
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }

            Uri uri = this.UriBuilder.BuildUri(command, settings, queryParams);
            DebugLogger.Instance.WriteLog("Calling {0}", uri.ToString());

            TimedRequest request = new TimedRequest(uri);
            this.AddRequestHeaders(request.WebRequest, requestHeaders);

            this.TryBuildRequestBody(
                (IAsyncResult ar) =>
                {
                    if (request.HasTimedOut)
                    {
                        return;
                    }

                    WebResponse response = null;
                    HttpWebResponse webResponse = null;
                    T responseItem = default(T);
                    HttpStatusCode? statusCode = null;
                    Exception error = null;
                    string responseBody = null;

                    try
                    {
                        response = request.WebRequest.EndGetResponse(ar);
                        webResponse = response as HttpWebResponse;
                        if (webResponse != null)
                        {
                            statusCode = webResponse.StatusCode;
                            command.SetAdditionalResponseInfo(new ResponseInfo(webResponse.ResponseUri, webResponse.Headers));

                            // Capture Server Time offset if we haven't already...
                            this.DeriveServerTimeOffset(webResponse.Headers);
                        }
                    }
                    catch (WebException ex)
                    {
                        DebugLogger.Instance.WriteVerboseInfo("Web Exception: {0} when calling {1}", ex, uri);
                        error = ex;
                        if (ex.Response != null)
                        {
                            response = ex.Response;
                            webResponse = (HttpWebResponse)ex.Response;
                            statusCode = webResponse.StatusCode;
                        }
                    }

                    string contentType = null;

                    if (response != null)
                    {
                        contentType = response.ContentType;
                        try
                        {
                            using (Stream responseStream = this.GzipHandler.GetResponseStream(response))
                            {
                                responseBody = responseStream.AsString();
                                responseItem = callback.ConvertFromRawResponse(responseBody);
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Instance.WriteVerboseInfo("Exception: {0} trying to handle response stream when calling {1}", ex, uri);
                            error = ex;
                            responseItem = default(T);
                        }
                    }

                    DoCallback(callback.Callback, responseItem, statusCode, contentType, error, responseBody, command.RequestId, uri, IsFake404(response));
                },
                () => DoCallback(callback.Callback, default(T), null, null, new ApiCallFailedException(), null, command.RequestId, uri),
                request,
                command);
        }

        /// <summary>
        /// Derives the server time offset from the Date and Age headers.
        /// </summary>
        /// <param name="headers">The response headers.</param>
#if OPEN_INTERNALS
        public
#else
        internal
#endif 
        void DeriveServerTimeOffset(WebHeaderCollection headers)
        {
            if (!this._obtainedServerTime)
            {
                if (headers != null)
                {
                    var timeHeader = headers["Date"];
                    DateTime serverTimeUtc;
                    if (!string.IsNullOrEmpty(timeHeader) && DateTime.TryParse(timeHeader, out serverTimeUtc))
                    {
                        serverTimeUtc = serverTimeUtc.ToUniversalTime();
                        int age;
                        var ageHeader = headers["Age"];
                        if (!string.IsNullOrEmpty(ageHeader) && int.TryParse(ageHeader, out age))
                        {
                            serverTimeUtc = serverTimeUtc.AddSeconds(age);
                        }

                        this._serverTimeOffset = serverTimeUtc.Subtract(DateTime.UtcNow);
                        this._obtainedServerTime = true;
                    }
                }
            }
        }

        /// <summary>
        /// Determines the difference between a 404 explicitly delivered by a service and a 404 response from WP while offline
        /// </summary>
        /// <param name="response">The web response</param>
        /// <returns>True if this 404 was provided by the WP platform without making a request</returns>
        private static bool IsFake404(WebResponse response)
        {
            return response != null && (response.ResponseUri == null || string.IsNullOrEmpty(response.ResponseUri.OriginalString));
        }

        /// <summary>
        /// Logs the response and makes the callback
        /// </summary>
        /// <typeparam name="T">The type of response item</typeparam>
        /// <param name="callback">The callback command</param>
        /// <param name="response">The response item</param>
        /// <param name="statusCode">The response status code</param>
        /// <param name="contentType">The response content type</param>
        /// <param name="error">An error or null if successful</param>
        /// <param name="responseBody">The response body</param>
        /// <param name="requestId">The unique id of this request</param>
        /// <param name="uri">The uri requested</param>
        /// <param name="offlineNotFoundResponse">Indicates whether this is a 'fake' 404 that WP can provide while offline</param>
        private static void DoCallback<T>(
                                       Action<Response<T>> callback,
                                       T response,
                                       HttpStatusCode? statusCode,
                                       string contentType,
                                       Exception error,
                                       string responseBody,
                                       Guid requestId,
                                       Uri uri,
                                       bool offlineNotFoundResponse = false)
        {
            DebugLogger.Instance.WriteLog("{0} response from {1}", statusCode.HasValue ? statusCode.ToString() : "Timeout", uri.ToString());

            if (!offlineNotFoundResponse && error != null)
            {
                DebugLogger.Instance.WriteException(
                    error,
                    new KeyValuePair<string, string>("uri", uri.ToString()),
                    new KeyValuePair<string, string>("errorResponseBody", responseBody),
                    new KeyValuePair<string, string>("statusCode", statusCode == null ? "Timeout" : statusCode.ToString()));
            }

            if (response != null && !offlineNotFoundResponse)
            {
                callback(new Response<T>(statusCode, contentType, response, requestId));
            }
            else
            {
                callback(new Response<T>(statusCode, error, responseBody, requestId));
            }
        }

        private void TryBuildRequestBody(AsyncCallback requestSuccessCallback, Action requestTimeoutCallback, TimedRequest request, MusicClientCommand apiMethod)
        {
            var requestBody = apiMethod.BuildRequestBody();
            request.WebRequest.Method = apiMethod.HttpMethod.ToString().ToUpperInvariant();
            if (requestBody != null)
            {
                var requestState = new RequestState(request, requestBody, requestSuccessCallback, requestTimeoutCallback);
                request.WebRequest.ContentType = apiMethod.ContentType;

                request.WebRequest.BeginGetRequestStream(this.RequestStreamCallback, requestState);
            }
            else
            {
                // No request body, just make the request immediately
                request.BeginGetResponse(requestSuccessCallback, requestTimeoutCallback, request);
            }
        }

        /// <summary>
        /// Writes request data to the request stream
        /// </summary>
        /// <param name="ar">The async response</param>
        private void RequestStreamCallback(IAsyncResult ar)
        {
            var requestState = (RequestState)ar.AsyncState;
            try
            {
                Stream streamResponse = requestState.TimedRequest.WebRequest.EndGetRequestStream(ar);
                byte[] byteArray = Encoding.UTF8.GetBytes(requestState.RequestBody);
                streamResponse.Write(byteArray, 0, byteArray.Length);
                streamResponse.Dispose();

                TimedRequest request = requestState.TimedRequest;
                request.BeginGetResponse(requestState.SuccessCallback, requestState.TimeoutCallback, request);
            }
            catch (WebException ex)
            {
                DebugLogger.Instance.WriteLog("WebException in RequestStreamCallback: {0}", ex);
                requestState.TimeoutCallback();
            }
        }

        private void AddRequestHeaders(WebRequest request, Dictionary<string, string> requestHeaders)
        {
            if (requestHeaders != null)
            {
                foreach (KeyValuePair<string, string> header in requestHeaders)
                {
                    DebugLogger.Instance.WriteLog(" Request Header: {0} = {1}", header.Key, header.Value);
                    request.Headers[header.Key] = header.Value;
                }
            }

            this.GzipHandler.EnableGzip(request);
        }

        private class RequestState
        {
            internal RequestState(TimedRequest request, string requestBody, AsyncCallback successCallback, Action timeoutCallback)
            {
                this.TimedRequest = request;
                this.RequestBody = requestBody;
                this.SuccessCallback = successCallback;
                this.TimeoutCallback = timeoutCallback;
            }

            internal TimedRequest TimedRequest { get; private set; }

            internal string RequestBody { get; private set; }

            internal AsyncCallback SuccessCallback { get; private set; }

            internal Action TimeoutCallback { get; private set; }
        }
    }
}

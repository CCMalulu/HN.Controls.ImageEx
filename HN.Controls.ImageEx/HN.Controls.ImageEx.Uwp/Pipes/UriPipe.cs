﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using HN.Extensions;
using Windows.Storage;

namespace HN.Pipes
{
    public class UriPipe<TResult> : PipeBase<TResult> where TResult : class
    {
        private readonly HttpMessageHandler _httpMessageHandler;

        public UriPipe(HttpMessageHandler httpMessageHandler)
        {
            if (httpMessageHandler == null)
            {
                throw new ArgumentNullException(nameof(httpMessageHandler));
            }

            _httpMessageHandler = httpMessageHandler;
        }

        public override async Task InvokeAsync(LoadingContext<TResult> context, PipeDelegate<TResult> next, CancellationToken cancellationToken = default(CancellationToken))
        {
            var uri = context.Current as Uri;
            if (uri == null)
            {
                await next(context, cancellationToken);
                return;
            }

            if (uri.IsHttp())
            {
                try
                {
                    var task = GetDownloadTask(uri, cancellationToken);
                    var (bytes, cacheControl) = await task;
                    context.Current = bytes;
                    await next(context, cancellationToken);
                    if (cacheControl != null && !cacheControl.NoCache)
                    {
                        context.HttpResponseBytes = bytes;
                    }
                }
                finally
                {
                    UriPipeInternal.DownloadTasks.Remove(uri);
                }
            }
            else if (string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                using (var fileStream = File.OpenRead(uri.AbsoluteUri.Substring("file:///".Length)))
                {
                    var buffer = new byte[fileStream.Length];
                    await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    context.Current = buffer;
                }
                await next(context, cancellationToken);
            }
            else
            {
                // ms-appx:/ or ms-appdata:/
                var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                var buffer = (await FileIO.ReadBufferAsync(file)).ToArray();
                context.Current = buffer;
                await next(context, cancellationToken);
            }
        }

        private async Task<(byte[], CacheControlHeaderValue)> CreateDownloadTask(Uri uri, CancellationToken cancellationToken)
        {
            using (var client = new HttpClient(_httpMessageHandler))
            {
                var response = await client.GetAsync(uri, cancellationToken);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync();
                var cacheControl = response.Headers.CacheControl;
                return (bytes, cacheControl);
            }
        }

        private Task<(byte[], CacheControlHeaderValue)> GetDownloadTask(Uri uri, CancellationToken cancellationToken)
        {
            lock (UriPipeInternal.DownloadTasks)
            {
                Task<(byte[], CacheControlHeaderValue)> task;
                if (UriPipeInternal.DownloadTasks.TryGetValue(uri, out task))
                {
                    return task;
                }

                task = CreateDownloadTask(uri, cancellationToken);
                UriPipeInternal.DownloadTasks[uri] = task;
                return task;
            }
        }
    }

    internal class UriPipeInternal
    {
        internal static readonly Dictionary<Uri, Task<(byte[], CacheControlHeaderValue)>> DownloadTasks = new Dictionary<Uri, Task<(byte[], CacheControlHeaderValue)>>();
    }
}
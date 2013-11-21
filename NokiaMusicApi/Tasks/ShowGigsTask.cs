﻿// -----------------------------------------------------------------------
// <copyright file="ShowGigsTask.cs" company="Nokia">
// Copyright (c) 2013, Nokia
// All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace Nokia.Music.Tasks
{
    /// <summary>
    /// Provides a simple way to show Nokia Music Gigs
    /// </summary>
    public sealed class ShowGigsTask : TaskBase
    {
        private string _searchTerms = null;

        /// <summary>
        /// Gets or sets optional search terms, such as an artist or city.
        /// </summary>
        /// <value>
        /// The search terms.
        /// </value>
        public string SearchTerms
        {
            get
            {
                return this._searchTerms;
            }

            set
            {
                this._searchTerms = value;
            }
        }

        /// <summary>
        /// Shows Gigs in Nokia Music
        /// </summary>
        public void Show()
        {
            if (!string.IsNullOrEmpty(this._searchTerms))
            {
                // No need to URI encode this one
                this.Launch(
                    new Uri("nokia-music://search/gigs/?term=" + this.SearchTerms),
                    new Uri("http://www.mixrad.io/"));
            }
            else
            {
                this.Launch(
                    new Uri("nokia-music://show/gigs/"),
                    new Uri("http://www.mixrad.io/"));
            }
        }
    }
}

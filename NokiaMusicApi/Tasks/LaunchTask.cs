﻿// -----------------------------------------------------------------------
// <copyright file="LaunchTask.cs" company="Nokia">
// Copyright (c) 2013, Nokia
// All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;
using Nokia.Music.Internal;

namespace Nokia.Music.Tasks
{
    /// <summary>
    /// Provides a simple way to show Nokia MixRadio
    /// </summary>
    public sealed class LaunchTask : TaskBase
    {
        /// <summary>
        /// Shows Nokia MixRadio
        /// </summary>
        public void Show()
        {
            this.Launch(new Uri("nokia-music://"), new Uri("http://www.mixrad.io/"));
        }
    }
}

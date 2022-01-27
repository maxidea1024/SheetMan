using SheetMan.Models;
using System;
using System.Collections.Generic;

namespace SheetMan
{
    /// <summary>
    /// SheetMan Exception.
    /// </summary>
    public class SheetManException : Exception
    {
        /// <summary>
        /// Detail error
        /// </summary>
        public class Detail
        {
            /// <summary>
            /// The sheet cell location where the error occurred.
            /// </summary>
            public Location Location { get; set; }

            /// <summary>
            /// Error message.
            /// </summary>
            public string Message { get; set; }
        }

        /// <summary>
        /// The sheet cell location where the error occurred.
        /// </summary>
        public Location Location { get; set; }

        /// <summary>
        /// Detail errors
        /// </summary>
        public List<Detail> Details { get; set; }

        /// <summary>
        /// Default empty constructor.
        /// </summary>
        public SheetManException()
        {
        }

        /// <summary>
        /// Construct with message.
        /// </summary>
        /// <param name="message"></param>
        public SheetManException(string message) : base(message)
        {
        }

        /// <summary>
        /// Construct with message and inner exception.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="inner"></param>
        public SheetManException(string message, Exception inner) : base(message, inner)
        {
        }

        /// <summary>
        /// Construct with cell-location and message.
        /// </summary>
        /// <param name="location"></param>
        /// <param name="message"></param>
        public SheetManException(Location location, string message) : base(message)
        {
            Location = location;
        }

        /// <summary>
        /// Construct with cell-location, message and inner exception.
        /// </summary>
        /// <param name="location"></param>
        /// <param name="message"></param>
        /// <param name="inner"></param>
        public SheetManException(Location location, string message, Exception inner) : base(message, inner)
        {
            Location = location;
        }
    }
}

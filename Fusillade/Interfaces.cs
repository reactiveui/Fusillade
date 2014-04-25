using System;
using System.Net.Http;
using Punchclock;
using Splat;

namespace Fusillade
{
    /// <summary>
    /// This enumeration defines the default base priorities associated with the
    /// different NetCache instances
    /// </summary>
    public enum Priority {
        Speculative = 10,
        UserInitiated = 100,
        Background = 20,
        Explicit = 0,
    }

    /// <summary>
    /// Limiting HTTP schedulers only allow a certain number of bytes to be
    /// read before cancelling all future requests. This is designed for
    /// reading data that may or may not be used by the user later, in order
    /// to improve response times should the user later request the data.
    /// </summary>
    public abstract class LimitingHttpMessageHandler : DelegatingHandler
    {
        public LimitingHttpMessageHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }
        public LimitingHttpMessageHandler() : base() { }

        /// <summary>
        /// Resets the total limit of bytes to read. This is usually called
        /// when the app resumes from suspend, to indicate that we should
        /// fetch another set of data.
        /// </summary>
        /// <param name="maxBytesToRead"></param>
        public abstract void ResetLimit(long? maxBytesToRead = null);
    }

    public static class NetCache
    {
        static NetCache()
        {
            var innerHandler = Locator.Current.GetService<HttpMessageHandler>();

            // NB: In vNext this value will be adjusted based on the user's
            // network connection, but that requires us to go fully platformy
            // like Splat.
            if (innerHandler == null) {
                speculative = new RateLimitedHttpMessageHandler(Priority.Speculative, 0, 1048576 * 5);
                userInitiated = new RateLimitedHttpMessageHandler(Priority.UserInitiated, 0);
                background = new RateLimitedHttpMessageHandler(Priority.Background, 0);
            } else {
                speculative = new RateLimitedHttpMessageHandler(innerHandler, Priority.Speculative, 0, 1048576 * 5);
                userInitiated = new RateLimitedHttpMessageHandler(innerHandler, Priority.UserInitiated, 0);
                background = new RateLimitedHttpMessageHandler(innerHandler, Priority.Background, 0);
            }
        }

        static LimitingHttpMessageHandler speculative;
        [ThreadStatic] static LimitingHttpMessageHandler unitTestSpeculative;

        /// <summary>
        /// Speculative HTTP schedulers only allow a certain number of bytes to be
        /// read before cancelling all future requests. This is designed for
        /// reading data that may or may not be used by the user later, in order
        /// to improve response times should the user later request the data.
        /// </summary>
        public static LimitingHttpMessageHandler Speculative
        {
            get { return unitTestSpeculative ?? speculative ?? Locator.Current.GetService<LimitingHttpMessageHandler>("Speculative"); }
            set {
                if (ModeDetector.InUnitTestRunner()) {
                    unitTestSpeculative = value;
                    speculative = speculative ?? value;
                } else {
                    speculative = value;
                }
            }
        }
                
        static HttpMessageHandler userInitiated;
        [ThreadStatic] static HttpMessageHandler unitTestUserInitiated;

        /// <summary>
        /// This scheduler should be used for requests initiated by a user
        /// action such as clicking an item, they have the highest priority.
        /// </summary>
        public static HttpMessageHandler UserInitiated
        {
            get { return unitTestUserInitiated ?? userInitiated ?? Locator.Current.GetService<HttpMessageHandler>("UserInitiated"); }
            set {
                if (ModeDetector.InUnitTestRunner()) {
                    unitTestUserInitiated = value;
                    userInitiated = userInitiated ?? value;
                } else {
                    userInitiated = value;
                }
            }
        }

        static HttpMessageHandler background;
        [ThreadStatic] static HttpMessageHandler unitTestBackground;

        /// <summary>
        /// This scheduler should be used for requests initiated in the
        /// background, and are scheduled at a lower priority.
        /// </summary>
        public static HttpMessageHandler Background
        {
            get { return unitTestBackground ?? background ?? Locator.Current.GetService<HttpMessageHandler>("Background"); }
            set {
                if (ModeDetector.InUnitTestRunner()) {
                    unitTestBackground = value;
                    background = background ?? value;
                } else {
                    background = value;
                }
            }
        }

        static OperationQueue operationQueue = new OperationQueue(4);
        [ThreadStatic] static OperationQueue unitTestOperationQueue;

        /// <summary>
        /// This scheduler should be used for requests initiated in the
        /// operationQueue, and are scheduled at a lower priority. You don't
        /// need to mess with this.
        /// </summary>
        public static OperationQueue OperationQueue
        {
            get { return unitTestOperationQueue ?? operationQueue ?? Locator.Current.GetService<OperationQueue>("OperationQueue"); }
            set {
                if (ModeDetector.InUnitTestRunner()) {
                    unitTestOperationQueue = value;
                    operationQueue = operationQueue ?? value;
                } else {
                    operationQueue = value;
                }
            }
        }
    }
}
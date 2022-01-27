using System;
using System.IO;

namespace SheetMan.Runtime
{
    public sealed class LiteBinaryException : IOException
    {
        public LiteBinaryException(string message) : base(message)
        {
        }

        public LiteBinaryException(string message, Exception inner) : base(message, inner)
        {
        }

        public static LiteBinaryException Misuse(string message)
        {
            return new LiteBinaryException(message);
        }

        public static LiteBinaryException MoreDataAvailable()
        {
            return new LiteBinaryException("Completed reading a message while more data was available in the stream.");
        }

        public static LiteBinaryException TruncatedMessage()
        {
            return new LiteBinaryException(
                "While reading a message, the input ended unexpectedly " +
                "in the middle of a field.  This could mean either that the " +
                "input has been truncated or that an embedded message " +
                "misreported its own length.");
        }

        public static LiteBinaryException NegativeSize()
        {
            return new LiteBinaryException("Encountered an embedded string or message which claimed to have negative size.");
        }

        public static LiteBinaryException MalformedVarint()
        {
            return new LiteBinaryException("Encountered a malformed varint.");
        }

        public static LiteBinaryException InvalidTag()
        {
            return new LiteBinaryException("Message contained an invalid tag.");
        }

        //@added
        public static LiteBinaryException MalformedFormat()
        {
            return new LiteBinaryException("Message is malformed.");
        }

        //@added
        public static LiteBinaryException CollectionCountLimited()
        {
            return new LiteBinaryException(
                "Collection count is exceeded.  " +
                "Set LiteBinaryConfig.MaxCollectionElementCount to increase the count limit.");
        }

        public static LiteBinaryException InvalidFieldId(int fieldId)
        {
            //result.success만 예외적으로 필드 아이디가 0이다.
            //오류 메시지를 어떻게 바꾸는게 좋을까?
            return new LiteBinaryException($"Field-id `{fieldId}` is an invalid value. The field-id must be in the range of 1 to 2 ^ 32-1.");
        }

        public static LiteBinaryException MessageInSizeLimited()
        {
            return new LiteBinaryException(
                "Message was too large.  May be malicious.  " +
                "Set IMessageIn.MessageMaxLength property or LiteBinaryConfig.MessageMaxLength to increase the size limit.");
        }

        public static LiteBinaryException RecursionLimitExceeded()
        {
            return new LiteBinaryException(
                "Message had too many levels of nesting.  May be malicious.  " +
                "Use IMessageIn.SetRecursionLimit() to increase the depth limit.");
        }

        public static LiteBinaryException UnderflowRecursionDepth()
        {
            return new LiteBinaryException("Recursion depth is mismatched. may be over call DecreaseRecursionDepth()");
        }

        public static LiteBinaryException RequiredFieldIsMissing(string fieldName, string structName)
        {
            return new LiteBinaryException($"The `{fieldName}` field was missing even though it was specified as a required field in structure `{structName}`.");
        }
    }
}

using System.Buffers;
using System.Threading.Channels;

namespace PasswordManagerLocal.Backend.Utils;

internal readonly record struct ChannelStreamPooledSegment(byte[] Buffer, int Length);

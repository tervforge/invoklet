using Xunit;

namespace Invoklet.Runtime.Tests;

// A script Invoklet runs asks it for one secret by name and gets the value, none, the reason the store can't be read, or a refusal; the
// store's key never reaches the script. Console and environment are process-wide: see ConsoleTestCollection.
[Collection(ConsoleTestCollection.Name)]
public sealed class UtilSecretRequestTests
{
    [Fact]
    public void GetSecret_AStoredSecret_ReturnsTheValueInvokletAnswers()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture(UtilSecretRequestFixture.Found("p@ss\nwörd"));

        // Act
        var value = Util.GetSecret(fixture.Name);

        // Assert
        Assert.Equal("p@ss\nwörd", value);
    }

    [Fact]
    public void GetSecret_AnyName_AsksInvokletToReadThatSecret()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture(UtilSecretRequestFixture.Found("x"));

        // Act
        Util.GetSecret(fixture.Name);

        // Assert
        using var request = fixture.FirstRequest();
        Assert.Equal(fixture.Name, request.RootElement.GetProperty("Secret").GetString());
        Assert.True(request.RootElement.GetProperty("Read").GetBoolean());
    }

    [Fact]
    public void GetSecret_AStoreInvokletCantRead_ThrowsWithItsReason()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture(UtilSecretRequestFixture.Unreadable("damaged"));

        // Act
        var thrown = Assert.Throws<InvalidDataException>(() => Util.GetSecret(fixture.Name));

        // Assert
        Assert.Equal("damaged", thrown.Message);
    }

    // Invoklet answers 3 when the user didn't let the script use the Secret Store.
    [Fact]
    public void GetSecret_TheUserDidntAllowIt_ThrowsAsNotAllowed()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture("3");

        // Act
        var read = () => Util.GetSecret(fixture.Name);

        // Assert
        Assert.Throws<UnauthorizedAccessException>(read);
    }

    [Fact]
    public void SetSecret_TheUserDidntAllowIt_ThrowsAsNotAllowed()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture("3");

        // Act
        var save = () => Util.SetSecret(fixture.Name, "x");

        // Assert
        Assert.Throws<UnauthorizedAccessException>(save);
    }

    // An empty answer to the question for a missing secret (the command line's stdin ended, say) is no secret.
    [Fact]
    public void GetSecret_AnEmptyAnswerForAMissingSecret_ThrowsAsMissing()
    {
        // Arrange
        using var fixture = new UtilSecretRequestFixture("0", "");

        // Act
        var read = () => Util.GetSecret(fixture.Name);

        // Assert
        Assert.Throws<KeyNotFoundException>(read);
    }

    [Theory]
    [InlineData("new value")]
    [InlineData(null)]
    public async Task SetSecret_WhileAReadIsInFlight_LeavesTheCacheAtTheConfirmedValue(string? value)
    {
        var timeout = TimeSpan.FromSeconds(15);
        // A long reply widens the interval between reading and decoding it. All data is synthetic.
        var oldValue = new string('a', 4 * 1024 * 1024);
        using var answers = new InFlightReadReplies(UtilSecretRequestFixture.Found(oldValue), timeout);
        using var fixture = new UtilSecretRequestFixture(answers);
        var read = Task.Factory.StartNew(() => Util.GetSecret(fixture.Name), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await answers.ReadStarted.WaitAsync(timeout);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var write = Task.Factory.StartNew(() =>
        {
            writeStarted.TrySetResult();
            Util.SetSecret(fixture.Name.ToUpperInvariant(), value);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await writeStarted.Task.WaitAsync(timeout);
        }
        finally
        {
            answers.ReleaseRead();
        }
        await Task.WhenAll(read, write).WaitAsync(timeout);

        Assert.Equal(oldValue, await read);
        if (value is null)
            Assert.Throws<KeyNotFoundException>(() => Util.GetSecret(fixture.Name));
        else
            Assert.Equal(value, Util.GetSecret(fixture.Name));
    }

    [Theory]
    [InlineData("db")]
    [InlineData("DB")]
    public void ReadStoredSecret_DuplicateNames_Throws(string secondName)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"Version\":1,\"Protection\":\"None\",\"Secrets\":{\"db\":\"a\",\"" + secondName + "\":\"b\"}}");

            Assert.Throws<InvalidDataException>(() => Util.ReadStoredSecret(path, "db", null));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadStoredSecret_AUniqueName_ReturnsTheValue()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"Version\":1,\"Protection\":\"None\",\"Secrets\":{\"db\":\"synthetic\"}}");

            Assert.Equal("synthetic", Util.ReadStoredSecret(path, "DB", null));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class InFlightReadReplies(string readReply, TimeSpan timeout) : TextReader
    {
        private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _releaseRead = new();
        private int _nextReply;

        public Task ReadStarted => _readStarted.Task;

        public void ReleaseRead() => _releaseRead.Set();

        public override string? ReadLine()
        {
            switch (_nextReply++)
            {
                case 0:
                    _readStarted.TrySetResult();
                    if (!_releaseRead.Wait(timeout)) throw new TimeoutException("The test did not release the read reply.");
                    return readReply;
                case 1:
                    return "1"; // the host confirmed the write or removal
                case 2:
                    return "0"; // the deleted secret is absent from the store
                default:
                    return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _releaseRead.Dispose();
            base.Dispose(disposing);
        }
    }
}

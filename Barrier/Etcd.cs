using System;
using System.Threading;
using System.Threading.Tasks;
using dotnet_etcd;

namespace Barrier;

class Etcd
{
    readonly string barrierId;
    readonly string metaKey;
    readonly string deadlineKey;
    readonly string stateKey;
    readonly string membersPrefix;

    readonly EtcdClient etcd;

    public Etcd(string connectionString, string barrierId = "barrier")
    {
        // dotnet-etcd uses gRPC; the client handles retries internally.
        this.etcd = new EtcdClient(connectionString);
        this.barrierId = barrierId;
        this.metaKey = $"/barriers/{barrierId}/meta";
        this.deadlineKey = $"/barriers/{barrierId}/deadline";
        this.stateKey = $"/barriers/{barrierId}/state";
        this.membersPrefix = $"/barriers/{barrierId}/members/";
    }

    async Task<bool> nodeExistsAsync(string key)
    {
        var response = await this.etcd.GetRangeAsync(key);
        return response.Kvs.Count > 0;
    }

    async Task<string> getNodeValueAsync(string key)
    {
        var response = await this.etcd.GetRangeAsync(key);
        if (response.Kvs.Count == 0)
            return null;
        return response.Kvs[0].Value.ToStringUtf8();
    }

    /// <summary>
    /// Creates the barrier if it doesn't exist. Uses a leader-election style approach
    /// where the first one to write the meta key wins.
    /// </summary>
    public async Task<bool> CreateBarrierAsync(int timeoutSeconds)
    {
        // Check if barrier already exists
        if (await this.nodeExistsAsync(this.metaKey))
            return false; // Barrier already created by another node

        long deadline = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + timeoutSeconds;
        string deadlineValue = deadline.ToString();
        string metaValue = $"{{\"created_at\": {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}, \"timeout_sec\": {timeoutSeconds}}}";

        try
        {
            // Try to create meta - this is our "winning" condition
            await this.etcd.PutAsync(this.metaKey, metaValue);

            // We won! Now create deadline and state
            await this.etcd.PutAsync(this.deadlineKey, deadlineValue);
            await this.etcd.PutAsync(this.stateKey, "open");

            return true;
        }
        catch
        {
            // Another node might have created it, that's OK
            return false;
        }
    }

    /// <summary>
    /// Enrolls a node in the barrier if the barrier is still open and deadline hasn't passed.
    /// </summary>
    public async Task<bool> EnrollAsync(string nodeId)
    {
        // Read current state and deadline
        var stateValue = await this.getNodeValueAsync(this.stateKey);
        var deadlineValue = await this.getNodeValueAsync(this.deadlineKey);

        if (stateValue == null || deadlineValue == null)
            return false; // Barrier doesn't exist

        if (stateValue != "open")
            return false; // Barrier already closed

        if (!long.TryParse(deadlineValue, out long deadline))
            return false; // Invalid deadline

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now >= deadline)
            return false; // Deadline has passed

        // Enroll by adding to members
        string memberKey = $"{this.membersPrefix}{nodeId}";

        try
        {
            await this.etcd.PutAsync(memberKey, string.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Closes the barrier by setting state to "closed".
    /// Any node can close it once the deadline is passed or if explicitly requested.
    /// </summary>
    public async Task<bool> CloseBarrierAsync()
    {
        try
        {
            var stateValue = await this.getNodeValueAsync(this.stateKey);
            if (stateValue != "open")
                return false; // Already closed

            await this.etcd.PutAsync(this.stateKey, "closed");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the current number of enrolled members.
    /// </summary>
    public async Task<int> GetMemberCountAsync()
    {
        var response = await this.etcd.GetRangeAsync(this.membersPrefix);
        return response.Kvs.Count;
    }

    /// <summary>
    /// Waits for the barrier to close by periodically checking the state key.
    /// Returns true if barrier closed, false if timeout occurred.
    /// </summary>
    public async Task<bool> WaitForBarrierAsync(int timeoutSeconds = 60)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var stateValue = await this.getNodeValueAsync(this.stateKey);
                if (stateValue == "closed")
                    return true;

                // Wait a bit before checking again
                await Task.Delay(100, cts.Token);
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            return false; // Timeout occurred
        }
    }
}

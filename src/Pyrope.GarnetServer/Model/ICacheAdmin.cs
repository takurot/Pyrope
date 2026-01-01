namespace Pyrope.GarnetServer.Model
{
    public interface ICacheAdmin
    {
        int Clear();
        int RemoveByPrefix(string prefix);
    }
}

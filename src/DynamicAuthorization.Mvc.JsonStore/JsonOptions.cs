namespace DynamicAuthorization.Mvc.JsonStore
{
    public class JsonOptions
    {
        public string FileName { get; set; } = "RoleAccess.json";

        public bool UseMemoryCache { get; set; } = true;
    }
}
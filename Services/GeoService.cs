namespace StaffWork_Track.Services;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class GeoService
{
    private readonly HttpClient _httpClient;
    private const string API_KEY = "YOUR_GOOGLE_API_KEY";

    public GeoService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetAddress(double lat, double lng)
    {
        try
        {
            var url = $"https://maps.googleapis.com/maps/api/geocode/json?latlng={lat},{lng}&key={API_KEY}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return $"{lat}, {lng}";

            var json = await response.Content.ReadAsStringAsync();
            var data = JObject.Parse(json);

            var results = data["results"];
            if (results == null || !results.HasValues)
                return $"{lat}, {lng}";

            // ✅ Full formatted address
            var address = results[0]?["formatted_address"]?.ToString();

            return address ?? $"{lat}, {lng}";
        }
        catch
        {
            return $"{lat}, {lng}";
        }
    }
} 
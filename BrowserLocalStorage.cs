using Microsoft.JSInterop;

namespace davesave_web
{
    public class BrowserLocalStorage
    {
        private readonly IJSRuntime JSRuntime;

        public BrowserLocalStorage(IJSRuntime runtime)
        {
            JSRuntime = runtime;
        }

        public async Task<string?> GetItem(string name)
        {
            Console.WriteLine("Fetching " + name);
            return await JSRuntime.InvokeAsync<string?>("localStorage.getItem", name);
        }

        public async Task SetItem(string name, string? value)
        {
            await JSRuntime.InvokeVoidAsync("localStorage.setItem", name, value);
        }

        public async Task RemoveItem(string name)
        {
            await JSRuntime.InvokeVoidAsync("localStorage.removeItem", name);
        }

        public async Task Clear()
        {
            await JSRuntime.InvokeVoidAsync("localStorage.clear");
        }
    }
}

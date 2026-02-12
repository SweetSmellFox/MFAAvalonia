using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 解析 MaaFramework 传递的 custom_action_param 字符串。
/// MaaFramework 可能将 JObject 序列化为双重编码的字符串（如 "{\"seconds\":60}"），
/// 此工具类会自动处理这种情况。
/// </summary>
public static class ActionParamHelper
{
    public static JObject Parse(string actionParam)
    {
        if (string.IsNullOrWhiteSpace(actionParam))
            return new JObject();

        // 先尝试直接解析为 JObject
        try
        {
            return JObject.Parse(actionParam);
        }
        catch
        {
            // 如果失败，可能是双重编码的字符串，先反序列化外层字符串
            try
            {
                var unwrapped = JsonConvert.DeserializeObject<string>(actionParam);
                if (!string.IsNullOrWhiteSpace(unwrapped))
                    return JObject.Parse(unwrapped);
            }
            catch
            {
                // ignored
            }
        }

        return new JObject();
    }
}

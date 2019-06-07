using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace tileset_compressor
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        public void clearMsg()
        {
            textBox1.Text = "";
        }

        public void addMsg(string text)
        {
            textBox1.AppendText(text + "\r\n");
        }

        private void button1_Click(object sender, EventArgs e)
        {
            clearMsg();
            clean();
            process();
            clean();
        }

        private void clean()
        {
            directory = null;
            data = null;
            bitmaps.ForEach(x=>
            {
                if (x != null) x.Dispose();
            });
            tilesets.Clear();
            floorIds.Clear();
            map.Clear();
            bitmaps.Clear();
            errors.Clear();
            now = 10000;
            closeBitmap();
        }

        private void process()
        {
            addMsg("------ 开始处理 ------");
            // --- Find directory
            string[] dirs = { ".", "project", "..\\project" };
            foreach (var dir in dirs)
            {
                if (File.Exists(Path.Combine(dir, "data.js"))
                    && Directory.Exists(Path.Combine(dir, "floors"))
                    && Directory.Exists(Path.Combine(dir, "images")))
                {
                    directory = dir;
                    break;
                }
            }
            if (directory == null)
            {
                addMsg("无法找到floors目录，images目录或data.js文件！");
                addMsg("------ 处理中断，退出 ------");
                return;
            }
            if (File.Exists(Path.Combine(directory, "data.backup.js")))
            {
                addMsg("本塔好像已经合并过了，请删除data.backup.js以确认重新进行合并。");
                addMsg("------ 处理中断，退出 ------");
                return;
            }

            addMsg("processing...");
            if (!readData()) return;
            if (!readFloors()) return;
            if (!writeTileset()) return;
            if (!writeData()) return;
            if (!processMaps()) return;
            addMsg("------ 处理完毕 ------");
            // errors.ForEach(addMsg);
            foreach (var error in errors) addMsg(error);
            addMsg("记得进入游戏和编辑器检查是否正常！");
        }

        private string directory = null;
        private string data = null;
        private List<string> floorIds = new List<string>();
        private List<string> tilesets = new List<string>();
        private Dictionary<int, int> map = new Dictionary<int, int>();
        private List<Bitmap> bitmaps = new List<Bitmap>();
        private HashSet<string> errors = new HashSet<string>();
        private int now = 10000;

        private bool readData()
        {
            addMsg("正在读取data.js文件......");
            data = string.Join("\r\n", File.ReadAllLines(Path.Combine(directory, "data.js")).Skip(1));
            try
            {
                JObject info = (JObject) JsonConvert.DeserializeObject(data);
                floorIds = ((JArray)info["main"]["floorIds"]).Values<string>().ToList();
                addMsg("本塔使用到的楼层ID： " + string.Join(", ", floorIds));
                tilesets = ((JArray) info["main"]["tilesets"]).Values<string>().ToList();
                addMsg("本塔使用到的tilesets素材： "+string.Join(", ", tilesets));
                addMsg("成功读取data.js文件！");
                return true;
            }
            catch (Exception e)
            {
                addMsg("ERROR: 无法处理data.js文件。");
                addMsg(e.ToString());
                addMsg("------ 处理中断，退出 ------");
                return false;
            }
        }

        private bool writeData()
        {
            addMsg("正在写入data.js文件......");
            try
            {
                File.Copy(Path.Combine(directory, "data.js"), Path.Combine(directory, "data.backup.js"), true);
                addMsg("备份 data.js -> data.backup.js");
                JObject info = (JObject)JsonConvert.DeserializeObject(data);
                JArray array = (JArray)info["main"]["tilesets"];
                if (bitmaps.Any())
                {
                    array.ReplaceAll("tilesets.min.png");
                    addMsg("替换tilesets为 tilesets.min.png");
                }
                else
                {
                    array.RemoveAll();
                    addMsg("移除tilesets项");
                }
                File.WriteAllText(Path.Combine(directory, "data.js"),
                    "var data_a1e2fb4a_e986_4524_b0da_9b7ba7c0874d = \r\n" + JsonConvert.SerializeObject(info, Formatting.Indented));
                addMsg("成功写入data.js文件！");
                return true;
            }
            catch (Exception e)
            {
                addMsg("ERROR: 无法写入data.js文件。");
                addMsg(e.ToString());
                addMsg("------ 处理中断，退出 ------");
                return false;
            }
        }

        private bool readFloors()
        {
            addMsg("正在处理楼层文件......");
            // string[] files = Directory.GetFiles(Path.Combine(directory, "floors"));
            Directory.CreateDirectory(Path.Combine(directory, "floors.backup"));
            foreach (var floorId in floorIds)
            {
                var filename = floorId + ".js";
                var path = Path.Combine(directory, "floors", filename);
                addMsg("正在处理 "+filename+"...");

                // --- 替换
                bool modified = false;
                try
                {
                    string content = string.Join("\r\n", File.ReadAllLines(path).Skip(1));
                    JObject info = (JObject) JsonConvert.DeserializeObject(content);
                    // 获得map, bgmap, fgmap
                    foreach (var s in new[] {"map", "bgmap", "fgmap"})
                    {
                        if (info[s] != null)
                        {
                            JArray array = (JArray) info[s];

                            JArray another = new JArray();

                            foreach (var child in array.Children())
                            {
                                JArray line = new JArray();
                                foreach (var i in child.Children())
                                {
                                    var v = i.ToObject<int>();
                                    if (v >= 10000)
                                    {
                                        modified = true;
                                        if (!map.ContainsKey(v))
                                        {
                                            addMsg("图块映射： "+v+" -> "+now);
                                            map[v] = now++;
                                            bitmaps.Add(getBitmap(v));
                                        }
                                        v = map[v];
                                    }
                                    line.Add(new JValue(v));
                                }
                                another.Add(line);
                            }
                            array.Replace(another);
                        }
                    }
                    if (modified)
                    {
                        File.Copy(path, Path.Combine(directory, "floors.backup", filename), true);
                        File.WriteAllText(path, "main.floors." + floorId + "=\r\n" + JsonConvert.SerializeObject(info, Formatting.Indented));
                        addMsg("备份 " + filename + " -> floors.backup，并写入文件... 成功");
                    }
                    else
                    {
                        addMsg(filename+"中不含有额外素材。");
                    }
                }
                catch (Exception e)
                {
                    addMsg("ERROR: 无法处理楼层文件" + filename + "。");
                    addMsg(e.ToString());
                    addMsg("------ 处理中断，退出 ------");
                    return false;
                }
            }
            addMsg("楼层文件处理完毕！");
            return true;
        }

        private Bitmap getBitmap(int number)
        {
            int id = number / 10000 - 1, index = number % 10000;
            if (id < 0 || id >= tilesets.Count)
            {
                var msg = "无法找到第" + id + "个额外素材，使用透明块代替";
                addMsg(msg);
                errors.Add(msg);
                return null;
            }
            openBitmap(tilesets[id]);
            if (tileset == null)
            {
                var msg = "无法打开额外素材" + tilesets[id] + "，使用透明块代替";
                addMsg(msg);
                errors.Add(msg);
                return null;
            }
            int width = tileset.Width, w = width / 32, h = tileset.Height / 32;
            int row = index / w, col = index % w;
            if (row >= h)
            {
                var msg = "素材" + number + "不在" + tilesets[id] + "上，使用透明块代替";
                addMsg(msg);
                errors.Add(msg);
                return null;
            }
            Bitmap bitmap = new Bitmap(32, 32);
            Graphics graphics = Graphics.FromImage(bitmap);
            graphics.DrawImage(tileset, new Rectangle(0, 0, 32, 32), col * 32, row * 32, 32, 32, GraphicsUnit.Pixel);
            graphics.Dispose();
            return bitmap;
            // return tileset.Clone(new Rectangle(row * 32, col * 32, 32, 32), tileset.PixelFormat);
        }

        private Bitmap tileset = null;
        private string tilesetName = null;
        private void openBitmap(string name)
        {
            if (tilesetName == name) return;
            closeBitmap();
            tilesetName = name;
            addMsg("正在打开素材文件"+tilesetName+"...");
            if (!File.Exists(Path.Combine(directory, "images", tilesetName)))
            {
                addMsg("ERROR: 无法打开文件" + tilesetName);
                return;
            }
            tileset = (Bitmap) Image.FromFile(Path.Combine(directory, "images", tilesetName));
        }

        private void closeBitmap()
        {
            if (tileset != null)
            {
                tileset.Dispose();
                tileset = null;
            }
            tilesetName = null;
        }

        private bool writeTileset()
        {
            closeBitmap();
            if (!bitmaps.Any())
            {
                addMsg("没有使用过的额外素材，不写入素材文件...");
                return true;
            }
            addMsg("开始写入tilesets.min.png...");
            var count = bitmaps.Count;
            int width = ((count - 1) / 1000 + 1) * 10, height = (count - 1) / width + 1;
            addMsg("素材总数："+count+"，构建"+width+"*"+height+"的素材。");
            var bitmap = new Bitmap(32 * width, 32 * height);
            Graphics graphics = Graphics.FromImage(bitmap);
            for (var i = 0; i < count; ++i)
            {
                var now = bitmaps[i];
                if (now == null) continue;
                graphics.DrawImage(now, new Point(32 * (i%width), 32 * (i/width)));
            }
            try
            {
                var path = Path.Combine(directory, "images", "tilesets.min.png");
                File.Delete(path);
                bitmap.Save(path, ImageFormat.Png);
                graphics.Dispose();
                bitmap.Dispose();
                addMsg("成功生成并写入tilesets.min.png！");
                return true;
            }
            catch (Exception e)
            {
                graphics.Dispose();
                bitmap.Dispose();
                addMsg("ERROR: 无法将tilesets.min.png写入到文件...");
                addMsg(e.ToString());
                addMsg("------ 处理中断，退出 ------");
                return false;
                
            }
        }

        private bool processMaps()
        {
            addMsg("读取并处理maps.js...");
            if (!File.Exists(Path.Combine(directory, "maps.js")))
            {
                addMsg("maps.js不存在，无需处理...");
                return true;
            }
            var content = string.Join("\r\n", File.ReadAllLines(Path.Combine(directory, "maps.js")).Skip(1));
            try
            {
                bool modified = false;
                JObject info = (JObject)JsonConvert.DeserializeObject(content);
                JObject another = new JObject();
                var enumerator = info.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var key = enumerator.Current.Key;
                    var value = enumerator.Current.Value;
                    try
                    {
                        int index = Convert.ToInt32(key);
                        if (index >= 10000)
                        {
                            if (map.ContainsKey(index))
                            {
                                modified = true;
                                var mapped = Convert.ToString(map[index]);
                                var cloned = value.DeepClone();
                                cloned["id"] = new JValue("X" + mapped);
                                another.Add(mapped, cloned);
                                continue;
                            }
                            addMsg("WARNING：maps.js中的"+index+"没有被映射！");
                        }
                        another.Add(key, value.DeepClone());
                    }
                    catch (Exception)
                    {
                        addMsg("无法识别maps.js中的"+key+"... 忽略");
                        another.Add(key, value.DeepClone());
                    }
                }
                enumerator.Dispose();

                if (modified)
                {
                    addMsg("备份maps.js -> maps.backup.js并写入... 成功");
                    File.Copy(Path.Combine(directory, "maps.js"), Path.Combine(directory, "maps.backup.js"), true);
                    File.WriteAllText(Path.Combine(directory, "maps.js"),
                        "var maps_90f36752_8815_4be8_b32b_d7fad1d0542e = \r\n" +
                        JsonConvert.SerializeObject(another, Formatting.Indented));
                }
                else
                {
                    addMsg("maps.js没有被修改");                    
                }
                return true;
            }
            catch (Exception e)
            {
                addMsg("ERROR: 无法处理maps.js文件。");
                addMsg(e.ToString());
                addMsg("------ 处理中断，退出 ------");
                return false;
            }
        }
    }
}

// Ncode — Russian programming language for games.
// Copyright (C) 2026 Ivproduction
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later.
// See LICENSE in the repository root.

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
#if WINDOWS
using System.Drawing;
using System.Windows.Forms;
#endif
using Ncode.Audio;
using Ncode.Core.Abstractions;
using Ncode.Core.Common;
using Ncode.Core.Lexer;
using Ncode.Platform;
using Ncode.Rendering;

public class Program
{
    static Program()
    {
#if ANDROID
        AudioService.Current = new AndroidAudioPlayer();
        GameHostService.Current = new AndroidGameHost();
        DialogService.Current = new AndroidDialogService();
        ClipboardService.Current = new AndroidClipboard();
#elif WINDOWS
        AudioService.Current = new WindowsAudioPlayer();
        GameHostService.Current = new WindowsFormsGameHost();
        DialogService.Current = new WindowsDialogService();
        ClipboardService.Current = new WindowsClipboard();
#else
        AudioService.Current = new NullAudioPlayer();
        GameHostService.Current = new NullGameHost();
        DialogService.Current = new NullDialogService();
        ClipboardService.Current = new NullClipboardService();
#endif
        try { Http.DefaultRequestHeaders.UserAgent.ParseAdd("Ncode/1.0"); } catch { }
        Http.Timeout = TimeSpan.FromSeconds(15);
        GameHostService.Current.OnKeyDown = k => HandleGameKeyDown(k);
        GameHostService.Current.OnPointerDown = (x, y) => HandleGameClick(x, y);
        GameHostService.Current.GetObjectsToRender = () =>
        {
            lock (GameObjectsLock)
            {
                var list = new List<RenderableObject>();
                foreach (var obj in GameObjects.Values)
                {
                    list.Add(new RenderableObject
                    {
                        Name = obj.Name,
                        SpritePath = obj.SpritePath,
                        X = obj.X,
                        Y = obj.Y,
                        Scale = obj.Scale,
                        Alpha = obj.Alpha,
                        Angle = obj.Angle,
                        Visible = obj.Visible,
                        PlatformData = obj.LoadedImage
                    });
                }
                return list;
            }
        };
    }

    static readonly object VarsLock = new();
    static Dictionary<string, object> Vars = new();
    static bool TryGetVar(string name, out object? val) { lock (VarsLock) return Vars.TryGetValue(name, out val); }
    static void SetVar(string name, object val) { lock (VarsLock) Vars[name] = val; }

    static Dictionary<string, List<List<Line>>> Handlers = new(StringComparer.OrdinalIgnoreCase);
    static List<List<Line>> OnStarts = new();
    static int BroadcastDepth = 0;

    class Trigger
    {
        public string Cond = "";
        public List<Line> Body = new();
        public int No;
        public bool Last;
    }
    static List<Trigger> Triggers = new();
    static int TriggerDepth = 0;

    enum ObjectMotion { None, Static, Dynamic }

class GameObject
    {
        public string Name = "";
        public string SpritePath = "";
#if WINDOWS
        public Image? LoadedImage = null;
#else
        public object? LoadedImage = null;
#endif
        public double X = 0;
        public double Y = 0;
        public double Scale = 100;
        public double Alpha = 0;
        public bool Visible = true;
        public ObjectMotion Motion = ObjectMotion.None;
        public Dictionary<string, object> Props = new(StringComparer.OrdinalIgnoreCase);

        public double VelocityX = 0;
        public double VelocityY = 0;
        public double AccelX = 0;
        public double AccelY = 0;
        public double Mass = 1.0;
        public double Damping = 0.0;
        public double Elasticity = 0.0;
        public double Friction = 0.1;
        public double Angle = 0;
        public double AngularVelocity = 0;
        public double MaxSpeed = double.MaxValue;
        public bool FreezeX = false;
        public bool FreezeY = false;
        public double ConstraintMinX = double.NegativeInfinity;
        public double ConstraintMaxX = double.PositiveInfinity;
        public double ConstraintMinY = double.NegativeInfinity;
        public double ConstraintMaxY = double.PositiveInfinity;
        public string? AttachedTo = null;
        public double AttachOffsetX = 0;
        public double AttachOffsetY = 0;
        public int CollisionLayer = 1;
        public int CollisionMask = 0xFF;
        public HashSet<string> IgnoredCollisions = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<List<Line>>> CollisionHandlers = new(StringComparer.OrdinalIgnoreCase);
        public List<List<Line>> OffScreenHandlers = new();
        public List<List<Line>> RestHandlers = new();
        public bool WasOffScreen = false;
        public bool WasAtRest = false;

        public (double X, double Y, double Width, double Height) GetBounds()
        {
            double scale = (Scale <= 0 ? 100.0 : Scale) / 100.0;
#if WINDOWS
            double w = LoadedImage is Image img ? img.Width * scale : Math.Max(24, 60 * scale);
            double h = LoadedImage is Image img2 ? img2.Height * scale : Math.Max(24, 60 * scale);
#else
            double w = Math.Max(24, 60 * scale);
            double h = Math.Max(24, 60 * scale);
#endif
            if (Props.TryGetValue("ширина", out var pw) || Props.TryGetValue("width", out pw))
            {
                try { w = Convert.ToDouble(pw, System.Globalization.CultureInfo.InvariantCulture); } catch { }
            }
            if (Props.TryGetValue("высота", out var ph) || Props.TryGetValue("height", out ph))
            {
                try { h = Convert.ToDouble(ph, System.Globalization.CultureInfo.InvariantCulture); } catch { }
            }
            return (X, Y, w, h);
        }

        public bool ContainsPoint(double px, double py)
        {
            if (!Visible) return false;
            var b = GetBounds();
            return px >= b.X && px <= b.X + b.Width && py >= b.Y && py <= b.Y + b.Height;
        }

        public double Speed => Math.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
    }

    static int WindowWidth = 800;
    static int WindowHeight = 600;
    static int GetCurrentWindowWidth() => GameHostService.Current.ClientWidth > 0 ? GameHostService.Current.ClientWidth : WindowWidth;
    static int GetCurrentWindowHeight() => GameHostService.Current.ClientHeight > 0 ? GameHostService.Current.ClientHeight : WindowHeight;
    static readonly object GameObjectsLock = new();
    static Dictionary<string, GameObject> GameObjects = new(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string, List<List<Line>>> KeyHandlers = new(StringComparer.OrdinalIgnoreCase);
    static readonly object ScriptThreadsLock = new();
    static List<Thread> ScriptThreads = new();
    static readonly System.Net.Http.HttpClient Http = new();

    static Regex NameRegex = new(@"^[A-Za-zА-Яа-яЁё_][A-Za-zА-Яа-яЁё0-9_]*$");
    static Regex QuotedFull = new("^\"([^\"]*)\"$");

    static string FirstWord(string s)
    {
        s = s.Trim();
        int sp = s.IndexOfAny(new[] { ' ', '\t' });
        return (sp < 0 ? s : s[..sp]).ToLowerInvariant();
    }
    static bool StartsWithWord(string text, params string[] words)
    {
        var t = text.Trim().ToLowerInvariant();
        foreach (var w in words)
        {
            if (t == w) return true;
            if (t.StartsWith(w + " ") || t.StartsWith(w + "\t")) return true;
        }
        return false;
    }
    static bool IsIf(string t) => StartsWithWord(t, "если", "эсли");
    static bool IsWhile(string t) => StartsWithWord(t, "пока");
    static bool IsRepeat(string t) => StartsWithWord(t, "повтори", "повторить", "повторять") && !StartsWithWord(t, "вечно");
    static bool IsFor(string t) => StartsWithWord(t, "для") && !StartsWithWord(t, "для каждого");
    static bool IsForEach(string t) => Regex.IsMatch(t.Trim(), @"^для\s+каждого\b", RegexOptions.IgnoreCase);
    static bool IsForever(string t) => Regex.IsMatch(t.Trim(), @"^вечно\s+повторя", RegexOptions.IgnoreCase);
    static bool IsWhen(string t) => StartsWithWord(t, "когда");
    static bool IsOnStart(string t) => StartsWithWord(t, "при запуске", "при старте");
    static bool IsOnClick(string t) => Regex.IsMatch(t.Trim(), @"^при\s+нажати(и|ях?)\b", RegexOptions.IgnoreCase);
    static bool IsTrigger(string t) => StartsWithWord(t, "как только");
    static bool IsElse(string t) => StartsWithWord(t, "иначе");
    static bool IsElseIf(string t)
    {
        var m = Regex.Match(t.Trim(), @"^иначе\b", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        return StartsWithWord(t.Trim()[m.Length..].Trim(), "если", "эсли");
    }
    static bool IsEnd(string t) => StartsWithWord(t, "конец");
    static bool IsBreak(string t) =>
        (StartsWithWord(t, "прервать", "прерви", "стоп") && !StartsWithWord(t, "стоп звук")) ||
        (StartsWithWord(t, "остановить", "останови") && !StartsWithWord(t, "остановить скрипт", "остановить скрипты", "остановить звук", "останови звук", "остановить движение", "останови движение"));
    static bool IsContinue(string t) => StartsWithWord(t, "продолжить", "продолжи") && !StartsWithWord(t, "продолжить звук", "продолжи звук");
    static bool IsExit(string t) => StartsWithWord(t, "выход", "выйти", "закончить", "завершить");
    static bool IsDeleteFile(string t) => Regex.IsMatch(t.Trim(), @"^удал(ить|и)\s+файл\b", RegexOptions.IgnoreCase);
    static bool IsRenameFile(string t) => Regex.IsMatch(t.Trim(), @"^переименовать\b", RegexOptions.IgnoreCase);
    static bool IsCopyFile(string t) => Regex.IsMatch(t.Trim(), @"^скопировать\s+файл\b", RegexOptions.IgnoreCase);
    static bool IsListFiles(string t) => Regex.IsMatch(t.Trim(), @"^список\s+файлов\b", RegexOptions.IgnoreCase);
    static bool IsCreateTable(string t)
    {
        var low = t.Trim().ToLowerInvariant();
        return low.StartsWith("создать таблицу") || low.StartsWith("создай таблицу") || low == "создать таблицу" || low == "создай таблицу";
    }
    static bool IsDeleteFromTable(string t) => Regex.IsMatch(t.Trim(), @"^удал(ить|и)\s+из\s+таблицы\b", RegexOptions.IgnoreCase);
    static bool IsDelete(string t) => StartsWithWord(t, "удалить", "удали");
    static bool IsShuffle(string t) => StartsWithWord(t, "перемешать", "перемешай");
    static bool IsInsert(string t) => StartsWithWord(t, "вставить", "вставь");
    static bool IsClear(string t) => StartsWithWord(t, "очистить", "очисти");
    static bool IsEvery(string t) => StartsWithWord(t, "каждые", "каждый");
    static bool IsTry(string t) => StartsWithWord(t, "попробовать", "попробуй");
    static bool IsCatch(string t) => StartsWithWord(t, "поймать", "поймай");
    static bool IsFileExists(string t) => Regex.IsMatch(t.Trim(), @"^есть\s+файл\b", RegexOptions.IgnoreCase);
    static bool IsCreateFolder(string t) => Regex.IsMatch(t.Trim(), @"^создать\s+папку\b", RegexOptions.IgnoreCase);
    static bool IsSplit(string t) => StartsWithWord(t, "разделить", "раздели");
    static bool IsJoin(string t) => StartsWithWord(t, "склеить", "склей");
    static bool IsCreateWindow(string t) => Regex.IsMatch(t.Trim(), @"^создать\s+окно\b", RegexOptions.IgnoreCase);
    static bool IsRunScene(string t) => Regex.IsMatch(t.Trim(), @"^запустить\s+сцену\b", RegexOptions.IgnoreCase);
    static bool IsRunScript(string t)
    {
        if (Regex.IsMatch(t.Trim(), @"^запустить\s+скрипт\b", RegexOptions.IgnoreCase)) return true;
        var after = Regex.Replace(t.Trim(), @"^запустить\s+", "", RegexOptions.IgnoreCase).Trim();
        return after != "" && !Regex.IsMatch(t.Trim(), @"^запустить\s+сцену\b", RegexOptions.IgnoreCase) &&
               (after.EndsWith(".ncode", StringComparison.OrdinalIgnoreCase) || after.Trim('"').EndsWith(".ncode", StringComparison.OrdinalIgnoreCase));
    }
    static bool IsStopScripts(string t) => Regex.IsMatch(t.Trim(), @"^остановить\s+скрипты\b", RegexOptions.IgnoreCase);
    static bool IsStopScript(string t)
    {
        if (Regex.IsMatch(t.Trim(), @"^остановить\s+скрипт\b", RegexOptions.IgnoreCase)) return true;
        var after = Regex.Replace(t.Trim(), @"^остановить\s+", "", RegexOptions.IgnoreCase).Trim();
        return after != "" && !Regex.IsMatch(t.Trim(), @"^остановить\s+скрипты\b", RegexOptions.IgnoreCase) &&
               (after.EndsWith(".ncode", StringComparison.OrdinalIgnoreCase) || after.Trim('"').EndsWith(".ncode", StringComparison.OrdinalIgnoreCase));
    }
    static bool IsWriteFile(string t) => StartsWithWord(t, "записать", "запиши");
    static bool IsReadFile(string t) => StartsWithWord(t, "прочитать", "прочитай");
    static bool IsInclude(string t) => StartsWithWord(t, "подключить", "подключи");
    static bool IsBroadcast(string t) => StartsWithWord(t, "вещать");
    static bool IsSetMotionType(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+тип\s+движения\b", RegexOptions.IgnoreCase);
    static bool IsSetSceneGravity(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+тяжесть\s+сцены\b", RegexOptions.IgnoreCase);
    static bool IsSetVelocity(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+скорость\b", RegexOptions.IgnoreCase);
    static bool IsSetVelocityX(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+скорость\s+х\b", RegexOptions.IgnoreCase) || Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+скорость\s+x\b", RegexOptions.IgnoreCase);
    static bool IsSetVelocityY(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+скорость\s+у\b", RegexOptions.IgnoreCase) || Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+скорость\s+y\b", RegexOptions.IgnoreCase);
    static bool IsAddVelocity(string t) => Regex.IsMatch(t.Trim(), @"^доба(вить|вь)\s+скорость\b", RegexOptions.IgnoreCase);
    static bool IsSetAcceleration(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+ускорение\b", RegexOptions.IgnoreCase);
    static bool IsStopMotion(string t) => Regex.IsMatch(t.Trim(), @"^остановить\s+движение\b", RegexOptions.IgnoreCase) || Regex.IsMatch(t.Trim(), @"^останови\s+движение\b", RegexOptions.IgnoreCase);
    static bool IsMoveBy(string t) => Regex.IsMatch(t.Trim(), @"^переместить\s+\S+\s+на\b", RegexOptions.IgnoreCase) || Regex.IsMatch(t.Trim(), @"^перемести\s+\S+\s+на\b", RegexOptions.IgnoreCase);
    static bool IsMoveTo(string t) => Regex.IsMatch(t.Trim(), @"^переместить\s+\S+\s+в\b", RegexOptions.IgnoreCase) || Regex.IsMatch(t.Trim(), @"^перемести\s+\S+\s+в\b", RegexOptions.IgnoreCase);
    static bool IsSetAngle(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+угол\b", RegexOptions.IgnoreCase);
    static bool IsRotateBy(string t) => Regex.IsMatch(t.Trim(), @"^поверн(уть|и)\s+\S+\s+на\b", RegexOptions.IgnoreCase);
    static bool IsSetAngularVelocity(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+угловую\s+скорость\b", RegexOptions.IgnoreCase);
    static bool IsApplyForce(string t) => Regex.IsMatch(t.Trim(), @"^примени(ть|й)?\s+силу\b", RegexOptions.IgnoreCase);
    static bool IsApplyExplosion(string t) => Regex.IsMatch(t.Trim(), @"^примени(ть|й)?\s+взрыв\b", RegexOptions.IgnoreCase);
    static bool IsAttractTo(string t) => Regex.IsMatch(t.Trim(), @"^притян(уть|и)\b", RegexOptions.IgnoreCase);
    static bool IsSetMass(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+масс[ую]\b", RegexOptions.IgnoreCase);
    static bool IsSetDamping(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+демпфирование\b", RegexOptions.IgnoreCase);
    static bool IsSetElasticity(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+упругость\b", RegexOptions.IgnoreCase);
    static bool IsSetFriction(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+трение\b", RegexOptions.IgnoreCase);
    static bool IsSetMaxSpeed(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+лимит\s+скорости\b", RegexOptions.IgnoreCase);
    static bool IsFreezeX(string t) => Regex.IsMatch(t.Trim(), @"^заморо(зить|зь)\s+х\b", RegexOptions.IgnoreCase) || Regex.IsMatch(t.Trim(), @"^заморо(зить|зь)\s+x\b", RegexOptions.IgnoreCase);
    static bool IsFreezeY(string t) => Regex.IsMatch(t.Trim(), @"^заморо(зить|зь)\s+у\b", RegexOptions.IgnoreCase) || Regex.IsMatch(t.Trim(), @"^заморо(зить|зь)\s+y\b", RegexOptions.IgnoreCase);
    static bool IsUnfreeze(string t) => Regex.IsMatch(t.Trim(), @"^разморо(зить|зь)\b", RegexOptions.IgnoreCase);
    static bool IsSetConstraintX(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+ограничение\s+х\b", RegexOptions.IgnoreCase) || Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+ограничение\s+x\b", RegexOptions.IgnoreCase);
    static bool IsSetConstraintY(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+ограничение\s+у\b", RegexOptions.IgnoreCase) || Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+ограничение\s+y\b", RegexOptions.IgnoreCase);
    static bool IsAttachTo(string t) => Regex.IsMatch(t.Trim(), @"^прикреп(ить|и)\b", RegexOptions.IgnoreCase);
    static bool IsDetach(string t) => Regex.IsMatch(t.Trim(), @"^открепить\b", RegexOptions.IgnoreCase) || Regex.IsMatch(t.Trim(), @"^открепи\b", RegexOptions.IgnoreCase);
    static bool IsSetCollisionLayer(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+слой\s+колли[зж]ий\b", RegexOptions.IgnoreCase);
    static bool IsSetCollisionMask(string t) => Regex.IsMatch(t.Trim(), @"^маска\s+колли[зж]ий\b", RegexOptions.IgnoreCase);
    static bool IsIgnoreCollision(string t) => Regex.IsMatch(t.Trim(), @"^игнорировать\s+колли[зж]ии\b", RegexOptions.IgnoreCase);
    static bool IsRestoreCollision(string t) => Regex.IsMatch(t.Trim(), @"^восстановить\s+колли[зж]ии\b", RegexOptions.IgnoreCase);
    static bool IsSceneBorder(string t) => Regex.IsMatch(t.Trim(), @"^граница\s+сцены\b", RegexOptions.IgnoreCase);
    static bool IsBounceOffEdge(string t) => Regex.IsMatch(t.Trim(), @"^отско(чить|чи)\s+\S+\s+от\s+края\b", RegexOptions.IgnoreCase);
    static bool IsSmoothMoveTo(string t) => Regex.IsMatch(t.Trim(), @"^плавно\s+переместить\b", RegexOptions.IgnoreCase);
    static bool IsGetDistance(string t) => Regex.IsMatch(t.Trim(), @"^расстояние\b", RegexOptions.IgnoreCase);
    static bool IsGetAngleTo(string t) => Regex.IsMatch(t.Trim(), @"^угол\s+к\b", RegexOptions.IgnoreCase);
    static bool IsCheckCollision(string t) => Regex.IsMatch(t.Trim(), @"^проверить\s+столкновение\b", RegexOptions.IgnoreCase);
    static bool IsObjectOnScreen(string t) => Regex.IsMatch(t.Trim(), @"^объект\s+на\s+экране\b", RegexOptions.IgnoreCase) || Regex.IsMatch(t.Trim(), @"^обьект\s+на\s+экране\b", RegexOptions.IgnoreCase);
    static bool IsGetSpeed(string t) => Regex.IsMatch(t.Trim(), @"^скорость\s+\S", RegexOptions.IgnoreCase) &&
        !Regex.IsMatch(t.Trim(), @"^скорость\s+сцены\b", RegexOptions.IgnoreCase);
    static bool IsOnCollision(string t) => Regex.IsMatch(t.Trim(), @"^при\s+столкновении\b", RegexOptions.IgnoreCase);
    static bool IsOnOffScreen(string t) => Regex.IsMatch(t.Trim(), @"^при\s+выходе\s+за\s+экран\b", RegexOptions.IgnoreCase);
    static bool IsOnRest(string t) => Regex.IsMatch(t.Trim(), @"^при\s+покое\b", RegexOptions.IgnoreCase);
    static bool IsSetDirection(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+направление\b", RegexOptions.IgnoreCase);
    static bool IsSetSpeedByDirection(string t) => Regex.IsMatch(t.Trim(), @"^зада(ть|й)\s+скорость\s+по\s+направлению\b", RegexOptions.IgnoreCase);
    static bool IsSet(string t) => StartsWithWord(t, "задать", "задай");
    static bool IsPrint(string t) => StartsWithWord(t, "вывести", "выведи", "напечатать", "напечатай", "печатать", "печатай", "показать", "покажи");
    static bool IsCreateList(string t)
    {
        var low = t.Trim().ToLowerInvariant();
        return low.StartsWith("создать список") || low.StartsWith("создай список") || low == "создать список" || low == "создай список";
    }
    static bool IsAdd(string t) => StartsWithWord(t, "добавить", "добавь");
    static bool IsAsk(string t) => StartsWithWord(t, "спросить", "спроси");
    static bool IsWait(string t) => StartsWithWord(t, "ждать", "жди");
    static bool IsKeyHandler(string t) =>
        StartsWithWord(t, "нажата клавиша", "когда нажата клавиша", "клавиша нажата", "нажата кнопка") ||
        (StartsWithWord(t, "нажата") && !StartsWithWord(t, "нажата_"));
    static bool IsDraw(string t) => StartsWithWord(t, "нарисовать", "нарисуй");
    static bool IsCreateObject(string t) => Regex.IsMatch(t.Trim(), @"^созда(ть|й)\s+(объект|обьект)\b", RegexOptions.IgnoreCase);
    static bool IsAssignImage(string t) => Regex.IsMatch(t.Trim(), @"^присво(ить|й)\s+образ\b", RegexOptions.IgnoreCase);
    static bool IsAssignProp(string t) => Regex.IsMatch(t.Trim(), @"^присво(ить|й)\s+свойство\b", RegexOptions.IgnoreCase);
    static bool IsPlaySound(string t) =>
        Regex.IsMatch(t.Trim(), @"^(воспроизвести|воспроизведи|играть|играй|сыграть|сыграй)(\s+звук)?\b", RegexOptions.IgnoreCase);
    static bool IsStopSound(string t) => Regex.IsMatch(t.Trim(), @"^(остановить|останови|стоп)\s+звук\b", RegexOptions.IgnoreCase);
    static bool IsPauseSound(string t) => Regex.IsMatch(t.Trim(), @"^(пауза\s+звук|пауза\b|приостановить\s+звук)", RegexOptions.IgnoreCase);
    static bool IsResumeSound(string t) => Regex.IsMatch(t.Trim(), @"^(продолжить|возобновить|возобнови)\s+звук\b", RegexOptions.IgnoreCase);
    static bool IsSetVolume(string t) => StartsWithWord(t, "громкость");
    static bool IsShowMessage(string t) => Regex.IsMatch(t.Trim(), @"^показ(ать|и)\s+сообщение\b", RegexOptions.IgnoreCase);
    static bool IsShowError(string t) => Regex.IsMatch(t.Trim(), @"^показ(ать|и)\s+ошибку\b", RegexOptions.IgnoreCase);
    static bool IsAskYesNo(string t) => Regex.IsMatch(t.Trim(), @"^спрос(ить|и)\s+(да\s+или\s+нет|да\/нет)\b", RegexOptions.IgnoreCase);
    static bool IsCopyClipboard(string t) => StartsWithWord(t, "скопировать", "скопируй");
    static bool IsPasteClipboard(string t) => Regex.IsMatch(t.Trim(), @"^вставить\s*(→|->|из\s+буфера\b)", RegexOptions.IgnoreCase);
    static bool IsHttpGet(string t) => Regex.IsMatch(t.Trim(), @"^получить\b", RegexOptions.IgnoreCase);
    static bool IsHttpSend(string t) => Regex.IsMatch(t.Trim(), @"^отправить\s+(POST|PUT|PATCH|DELETE)\b", RegexOptions.IgnoreCase);
    static bool IsFirebaseWrite(string t)
    {
        if (!StartsWithWord(t, "записать", "запиши")) return false;
        string rest = AfterFirstWord(t).Trim();
        if (rest.StartsWith("\"http://", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("\"https://", StringComparison.OrdinalIgnoreCase))
            return true;
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count >= 1 && TryResolveVar(parts[0], out var val) && val is string s && (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }
    static bool IsFirebaseRead(string t)
    {
        if (!StartsWithWord(t, "прочитать", "прочитай")) return false;
        string rest = AfterFirstWord(t).Trim();
        if (rest.StartsWith("\"http://", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("\"https://", StringComparison.OrdinalIgnoreCase))
            return true;
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count >= 1 && TryResolveVar(parts[0], out var val) && val is string s && (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }
    static bool IsFirebaseDelete(string t)
    {
        if (!StartsWithWord(t, "удалить", "удали")) return false;
        string rest = AfterFirstWord(t).Trim();
        if (rest.StartsWith("\"http://", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("\"https://", StringComparison.OrdinalIgnoreCase))
            return true;
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count >= 1 && TryResolveVar(parts[0], out var val) && val is string s && (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }
    static bool IsBlockStart(string t) => IsIf(t) || IsWhile(t) || IsRepeat(t) || IsFor(t) || IsForEach(t) || IsForever(t) || IsWhen(t) || IsOnStart(t) || IsOnClick(t) || IsTrigger(t) || IsEvery(t) || IsTry(t) || IsKeyHandler(t) || IsOnCollision(t) || IsOnOffScreen(t) || IsOnRest(t);

    static string AfterFirstWord(string text)
    {
        text = text.Trim();
        int sp = text.IndexOfAny(new[] { ' ', '\t' });
        return sp < 0 ? "" : text[(sp + 1)..].Trim();
    }

    static string StripTrailingWord(string s, params string[] words)
    {
        s = s.Trim();
        foreach (var w in words)
        {
            var m = Regex.Match(s, @"^(.*)[\s\t]+" + Regex.Escape(w) + @"[\s\t]*$", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();
        }
        return s;
    }

    static string Fmt(object v)
    {
        if (v is bool b) return b ? "истина" : "ложь";
        if (v is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (v is List<object> l) return "[" + string.Join(", ", l.Select(Fmt)) + "]";
        if (v is Dictionary<string, object> dict)
            return "{" + string.Join(", ", dict.Select(kv => $"\"{kv.Key}\": {Fmt(kv.Value)}")) + "}";
        return v.ToString() ?? "";
    }
    static bool IsTrue(object v)
    {
        if (v is bool b) return b;
        if (v is int i) return i != 0;
        if (v is double d) return Math.Abs(d) > 1e-9;
        if (v is string s) return s.Length > 0;
        if (v is List<object> l) return l.Count > 0;
        return v != null;
    }
    static double ToNum(object v, int line)
    {
        if (v is int i) return i;
        if (v is double d) return d;
        if (v is bool b) return b ? 1 : 0;
        throw new Exception($"строка {line}: надо число, а там '{Fmt(v)}'");
    }
    static object NormNum(double d)
    {
        if (Math.Abs(d - Math.Round(d)) < 1e-9 && d >= int.MinValue && d <= int.MaxValue)
            return (int)Math.Round(d);
        return d;
    }


    static object EvalTake(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^взять\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^из\s+", "", RegexOptions.IgnoreCase).Trim();
        int sp = t.IndexOfAny(new[] { ' ', '\t' });
        if (sp < 0) throw new Exception($"строка {line}: надо так -> взять из фрукты 1");
        string name = t[..sp].Trim();
        string idxExpr = t[(sp + 1)..].Trim();
        if (!NameRegex.IsMatch(name)) throw new Exception($"строка {line}: плохое имя списка '{name}'");
        if (idxExpr == "") throw new Exception($"строка {line}: надо так -> взять из {name} 1");
        if (!Vars.TryGetValue(name, out var v) || v is not List<object> l)
            throw new Exception($"строка {line}: нет такого списка: {name}. Сначала: создать список {name}");
        int idx = (int)Math.Round(ToNum(EvalArith(idxExpr, line), line));
        if (idx < 1 || idx > l.Count) throw new Exception($"строка {line}: в списке '{name}' всего {l.Count}, а просят {idx} (счет с 1)");
        return l[idx - 1];
    }

    static object EvalLen(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^длина\b", "", RegexOptions.IgnoreCase).Trim();
        if (t == "") throw new Exception($"строка {line}: надо так -> длина фрукты");
        object v = EvalArith(t, line);
        if (v is List<object> l) return l.Count;
        if (v is string s) return s.Length;
        throw new Exception($"строка {line}: длина только для списка или текста");
    }

    static readonly Random Rnd = new();
    static readonly System.Diagnostics.Stopwatch ProgTime = System.Diagnostics.Stopwatch.StartNew();

    static object EvalRandom(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^случайное\b", "", RegexOptions.IgnoreCase).Trim();
        var m = Regex.Match(t, @"^от\s+(.+?)\s+до\s+(.+)$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new Exception($"строка {line}: надо так -> случайное от 1 до 10");
        double a = ToNum(EvalArith(m.Groups[1].Value.Trim(), line), line);
        double b = ToNum(EvalArith(m.Groups[2].Value.Trim(), line), line);
        if (a > b) (a, b) = (b, a);
        bool ints = Math.Abs(a - Math.Round(a)) < 1e-9 && Math.Abs(b - Math.Round(b)) < 1e-9;
        if (ints) return Rnd.Next((int)a, (int)b + 1);
        return NormNum(a + Rnd.NextDouble() * (b - a));
    }

    static object EvalHas(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^есть\b", "", RegexOptions.IgnoreCase).Trim();
        int split = -1;
        bool inQ = false;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if ((t[i] == 'в' || t[i] == 'В') && (i == 0 || !IsWordChar(t[i - 1])) && (i + 1 >= t.Length || !IsWordChar(t[i + 1])))
                split = i;
        }
        if (split < 0) throw new Exception($"строка {line}: надо так -> есть \"яблоко\" в фрукты");
        string valExpr = t[..split].Trim();
        string name = t[(split + 1)..].Trim();
        if (valExpr == "" || !NameRegex.IsMatch(name))
            throw new Exception($"строка {line}: надо так -> есть \"яблоко\" в фрукты");
        if (!Vars.TryGetValue(name, out var v) || v is not List<object> l)
            throw new Exception($"строка {line}: нет такого списка: {name}");
        object want = EvalFull(valExpr, line);
        foreach (var item in l)
        {
            if (want is int or double or bool && item is int or double or bool)
            {
                if (Math.Abs(ToNum(want, line) - ToNum(item, line)) < 1e-9) return true;
            }
            else if (Fmt(item) == Fmt(want)) return true;
        }
        return false;
    }

    static object EvalMathFn(string expr, int line)
    {
        string t = expr.Trim();
        string fn = FirstWord(t).ToLowerInvariant();
        string arg = AfterFirstWord(t);
        if (fn is "максимум" or "минимум" or "степень" or "остаток")
        {
            var parts2 = SplitArgsPreservingQuotes(arg);
            if (parts2.Count < 2) throw new Exception($"строка {line}: надо так -> {fn} a b");
            double a2 = ToNum(EvalArith(parts2[0], line), line);
            double b2 = ToNum(EvalArith(parts2[1], line), line);
            return fn switch
            {
                "максимум" => NormNum(Math.Max(a2, b2)),
                "минимум" => NormNum(Math.Min(a2, b2)),
                "степень" => NormNum(Math.Pow(a2, b2)),
                "остаток" => b2 == 0 ? throw new Exception($"строка {line}: деление на ноль") : NormNum(a2 % b2),
                _ => throw new Exception($"строка {line}: не знаю '{fn}'")
            };
        }
        if (arg == "") throw new Exception($"строка {line}: надо так -> {fn} 16");
        double x = ToNum(EvalArith(arg, line), line);
        return fn switch
        {
            "корень" => x < 0 ? throw new Exception($"строка {line}: корень из отрицательного") : NormNum(Math.Sqrt(x)),
            "модуль" => NormNum(Math.Abs(x)),
            "округлить" => (int)Math.Round(x, MidpointRounding.AwayFromZero),
            "пол" => (int)Math.Floor(x),
            "потолок" => (int)Math.Ceiling(x),
            "синус" => NormNum(Math.Sin(x * Math.PI / 180.0)),
            "косинус" => NormNum(Math.Cos(x * Math.PI / 180.0)),
            "тангенс" => NormNum(Math.Tan(x * Math.PI / 180.0)),
            "логарифм" => x <= 0 ? throw new Exception($"строка {line}: логарифм из неположительного") : NormNum(Math.Log(x)),
            _ => throw new Exception($"строка {line}: не знаю '{fn}'")
        };
    }

    static int SplitLastStandalone(string s, string word, int line)
    {
        int found = -1;
        bool inQ = false;
        string low = s.ToLowerInvariant();
        string w = word.ToLowerInvariant();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if (i + w.Length <= low.Length && low.Substring(i, w.Length) == w)
            {
                bool lb = i == 0 || !IsWordChar(s[i - 1]);
                int e = i + w.Length;
                bool rb = e >= s.Length || !IsWordChar(s[e]);
                if (lb && rb) found = i;
            }
        }
        return found;
    }

    static object EvalFind(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^найти\b", "", RegexOptions.IgnoreCase).Trim();
        if (SplitFileTarget(t, line, out string what, out string where) < 0 || what == "" || where == "")
            throw new Exception($"строка {line}: надо так -> найти \"мир\" в \"привет мир\"");
        string hs = Fmt(EvalFull(where, line));
        string nd = Fmt(EvalFull(what, line));
        return hs.IndexOf(nd, StringComparison.Ordinal) + 1;
    }

    static object EvalReplace(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^заменить\b", "", RegexOptions.IgnoreCase).Trim();
        if (SplitFileTarget(t, line, out string left, out string where) < 0 || left == "" || where == "")
            throw new Exception($"строка {line}: надо так -> заменить \"мир\" на \"друг\" в \"привет мир\"");
        int ni = SplitLastStandalone(left, "на", line);
        if (ni < 0) throw new Exception($"строка {line}: надо так -> заменить \"мир\" на \"друг\" в \"привет мир\"");
        string what = left[..ni].Trim();
        string by = left[(ni + 2)..].Trim();
        if (what == "" || by == "")
            throw new Exception($"строка {line}: надо так -> заменить \"мир\" на \"друг\" в \"привет мир\"");
        return Fmt(EvalFull(where, line)).Replace(Fmt(EvalFull(what, line)), Fmt(EvalFull(by, line)));
    }

    static object EvalSlice(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^срез\b", "", RegexOptions.IgnoreCase).Trim();
        int si = SplitLastStandalone(t, "с", line);
        if (si < 0) throw new Exception($"строка {line}: надо так -> срез \"привет\" с 2 по 4");
        string textExpr = t[..si].Trim();
        string rest = t[(si + 1)..].Trim();
        int pi = SplitLastStandalone(rest, "по", line);
        if (pi < 0 || textExpr == "") throw new Exception($"строка {line}: надо так -> срез \"привет\" с 2 по 4");
        string s = Fmt(EvalFull(textExpr, line));
        int a = (int)Math.Round(ToNum(EvalArith(rest[..pi].Trim(), line), line));
        int b = (int)Math.Round(ToNum(EvalArith(rest[(pi + 2)..].Trim(), line), line));
        if (a < 1) a = 1;
        if (b > s.Length) b = s.Length;
        if (a > b) return "";
        return s.Substring(a - 1, b - a + 1);
    }

    static object EvalCase(string expr, int line)
    {
        string t = expr.Trim();
        bool up = StartsWithWord(t, "верхний");
        string arg = AfterFirstWord(t);
        if (arg == "") throw new Exception($"строка {line}: надо так -> верхний \"привет\"");
        string s = Fmt(EvalArith(arg, line));
        return up ? s.ToUpperInvariant() : s.ToLowerInvariant();
    }

    static bool TryParseBracketAccess(string expr, int line, out object? result)
    {
        result = null;
        string t = expr.Trim();
        if (!t.EndsWith("]")) return false;
        int ob = -1;
        bool inQ = false;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if (t[i] == '[') { ob = i; break; }
        }
        if (ob <= 0) return false;
        int cb = -1;
        inQ = false;
        int depth = 0;
        for (int i = ob; i < t.Length; i++)
        {
            if (t[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if (t[i] == '[') depth++;
            else if (t[i] == ']')
            {
                depth--;
                if (depth == 0) { cb = i; break; }
            }
        }
        if (cb != t.Length - 1) return false;
        string name = t[..ob].Trim();
        if (!NameRegex.IsMatch(name)) return false;
        string inner = t[(ob + 1)..^1].Trim();
        if (!TryResolveVar(name, out var target)) return false;
        if (target is Dictionary<string, object> dict)
        {
            string key = Fmt(EvalFull(inner, line));
            if (!dict.TryGetValue(key, out var val))
                throw new Exception($"строка {line}: в таблице '{name}' нет ключа '{key}'");
            result = val;
            return true;
        }
        if (target is List<object> list)
        {
            int idx = (int)Math.Round(ToNum(EvalArith(inner, line), line));
            if (idx < 1 || idx > list.Count)
                throw new Exception($"строка {line}: в списке '{name}' всего {list.Count}, а просят {idx} (счет с 1)");
            result = list[idx - 1];
            return true;
        }
        return false;
    }

    static string ResolveBracketAccessesInExpr(string expr, int line)
    {
        while (true)
        {
            int ob = -1;
            int startName = -1;
            bool inQ = false;
            for (int i = 0; i < expr.Length; i++)
            {
                if (expr[i] == '"') { inQ = !inQ; continue; }
                if (inQ) continue;
                if (expr[i] == '[')
                {
                    int s = i - 1;
                    while (s >= 0 && (char.IsLetterOrDigit(expr[s]) || expr[s] == '_' || expr[s] == '.')) s--;
                    s++;
                    if (s < i)
                    {
                        string candidate = expr[s..i].Trim();
                        if (NameRegex.IsMatch(candidate))
                        {
                            ob = i;
                            startName = s;
                            break;
                        }
                    }
                }
            }
            if (ob < 0 || startName < 0) break;
            string varName = expr[startName..ob].Trim();

            int cb = -1;
            inQ = false;
            int depth = 0;
            for (int i = ob; i < expr.Length; i++)
            {
                if (expr[i] == '"') { inQ = !inQ; continue; }
                if (inQ) continue;
                if (expr[i] == '[') depth++;
                else if (expr[i] == ']')
                {
                    depth--;
                    if (depth == 0) { cb = i; break; }
                }
            }
            if (cb < 0) break;

            string inner = expr[(ob + 1)..cb].Trim();
            if (!TryResolveVar(varName, out var target)) break;

            object? val;
            if (target is Dictionary<string, object> dict)
            {
                string key = Fmt(EvalFull(inner, line));
                if (!dict.TryGetValue(key, out val))
                    throw new Exception($"строка {line}: в таблице '{varName}' нет ключа '{key}'");
            }
            else if (target is List<object> list)
            {
                int idx = (int)Math.Round(ToNum(EvalArith(inner, line), line));
                if (idx < 1 || idx > list.Count)
                    throw new Exception($"строка {line}: в списке '{varName}' всего {list.Count}, а просят {idx} (счет с 1)");
                val = list[idx - 1];
            }
            else break;

            string lit = val switch
            {
                string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"",
                bool b => b ? "истина" : "ложь",
                double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                int iv => iv.ToString(),
                null => "\"\"",
                _ => Fmt(val)
            };

            expr = expr[..startName] + lit + expr[(cb + 1)..];
        }
        return expr;
    }

    static object EvalArith(string expr, int line)
    {
        expr = expr.Trim();
        if (expr == "") throw new Exception($"строка {line}: пустое выражение");
        if (TryParseBracketAccess(expr, line, out object? directVal)) return directVal!;
        if (expr.Contains('[')) expr = ResolveBracketAccessesInExpr(expr, line);
        if (StartsWithWord(expr, "взять")) return EvalTake(expr, line);
        if (StartsWithWord(expr, "длина")) return EvalLen(expr, line);
        if (StartsWithWord(expr, "есть ключ")) return EvalHasKey(expr, line);
        if (StartsWithWord(expr, "есть файл")) return EvalFileExists(expr, line);
        if (StartsWithWord(expr, "есть")) return EvalHas(expr, line);
        if (StartsWithWord(expr, "корень", "модуль", "округлить", "пол", "потолок", "синус", "косинус", "тангенс", "логарифм", "максимум", "минимум", "степень", "остаток")) return EvalMathFn(expr, line);
        if (StartsWithWord(expr, "найти")) return EvalFind(expr, line);
        if (StartsWithWord(expr, "заменить")) return EvalReplace(expr, line);
        if (StartsWithWord(expr, "срез")) return EvalSlice(expr, line);
        if (StartsWithWord(expr, "верхний", "нижний")) return EvalCase(expr, line);
        if (StartsWithWord(expr, "случайное")) return EvalRandom(expr, line);
        if (StartsWithWord(expr, "заглавные")) return Fmt(EvalFull(AfterFirstWord(expr), line)).ToUpperInvariant();
        if (StartsWithWord(expr, "строчные")) return Fmt(EvalFull(AfterFirstWord(expr), line)).ToLowerInvariant();
        if (StartsWithWord(expr, "обрезать")) return Fmt(EvalFull(AfterFirstWord(expr), line)).Trim();
        if (StartsWithWord(expr, "содержит")) return EvalStrContains(expr, line);
        if (StartsWithWord(expr, "начинается")) return EvalStrStartsWith(expr, line);
        if (StartsWithWord(expr, "заканчивается")) return EvalStrEndsWith(expr, line);
        if (StartsWithWord(expr, "повторить")) return EvalStrRepeat(expr, line);
        if (StartsWithWord(expr, "ключи")) return EvalDictKeys(expr, line);
        if (StartsWithWord(expr, "значения")) return EvalDictValues(expr, line);
        if (StartsWithWord(expr, "тип")) return EvalTypeOf(expr, line);
        if (StartsWithWord(expr, "json")) return EvalJsonParse(expr, line);
        if (StartsWithWord(expr, "в json")) return EvalToJson(expr, line);
        if (StartsWithWord(expr, "имя файла")) return EvalFileName(expr, line);
        if (StartsWithWord(expr, "расширение")) return EvalFileExt(expr, line);
        if (StartsWithWord(expr, "путь к папке")) return EvalFileDir(expr, line);
        if (expr.Trim().Equals("дата", StringComparison.OrdinalIgnoreCase)) return DateTime.Now.ToString("yyyy-MM-dd");
        if (expr.Trim().Equals("время", StringComparison.OrdinalIgnoreCase)) return DateTime.Now.ToString("HH:mm:ss");
        if (expr.Trim().Equals("миллисекунды", StringComparison.OrdinalIgnoreCase)) return NormNum(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (expr.Trim().Equals("секунды с запуска", StringComparison.OrdinalIgnoreCase)) return NormNum(Math.Round(ProgTime.Elapsed.TotalSeconds, 3));
        var q = QuotedFull.Match(expr);
        if (q.Success) return q.Groups[1].Value;

        var toks = Tokenizer.Tokenize(expr, line);
        if (toks.Count == 0) throw new Exception($"строка {line}: пустое выражение");
        if (toks.Count == 1)
        {
            var t = toks[0];
            if (t.Kind == TokKind.Num)
            {
                if (int.TryParse(t.Val, out int ii)) return ii;
                if (double.TryParse(t.Val, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double dd)) return NormNum(dd);
                throw new Exception($"строка {line}: плохое число '{t.Val}'");
            }
            if (t.Kind == TokKind.Str) return t.Val;
            if (t.Kind == TokKind.Bool) return t.Val == "true";
            if (t.Kind == TokKind.Name)
            {
                if (!TryResolveVar(t.Val, out var v))
                    throw new Exception($"строка {line}: нет такой переменной: {t.Val}. Текст надо в кавычках: \"{t.Val}\"");
                return v!;
            }
            throw new Exception($"строка {line}: не понимаю '{expr}'");
        }
        if (toks.Count == 3 && toks[0].Kind == TokKind.Op && toks[1].Kind != TokKind.Op && toks[2].Kind != TokKind.Op
            && toks[1].Kind != TokKind.LPar && toks[2].Kind != TokKind.LPar)
        {
            object a = SingleTokValue(toks[1], line);
            object b = SingleTokValue(toks[2], line);
            return ApplyOp(toks[0].Val, a, b, line);
        }
        var rpn = ToRpn(toks, line);
        return EvalRpn(rpn, line);
    }

    static object EvalHasKey(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^есть\s+ключ\b", "", RegexOptions.IgnoreCase).Trim();
        int inIdx = SplitLastStandalone(t, "в", line);
        if (inIdx < 0) throw new Exception($"строка {line}: надо так -> есть ключ \"имя\" в таблица");
        string keyExpr = t[..inIdx].Trim();
        string tableName = t[(inIdx + 1)..].Trim();
        if (!TryResolveVar(tableName, out var tv) || tv is not Dictionary<string, object> dict)
            throw new Exception($"строка {line}: '{tableName}' не таблица");
        string key = Fmt(EvalFull(keyExpr, line));
        return dict.ContainsKey(key);
    }

    static object EvalStrContains(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^содержит\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(t);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> содержит \"строка\" \"подстрока\"");
        string haystack = Fmt(EvalFull(parts[0], line));
        string needle = Fmt(EvalFull(parts[1], line));
        return haystack.Contains(needle, StringComparison.Ordinal);
    }

    static object EvalStrStartsWith(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^начинается\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(t);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> начинается \"строка\" \"префикс\"");
        return Fmt(EvalFull(parts[0], line)).StartsWith(Fmt(EvalFull(parts[1], line)), StringComparison.Ordinal);
    }

    static object EvalStrEndsWith(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^заканчивается\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(t);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> заканчивается \"строка\" \"суффикс\"");
        return Fmt(EvalFull(parts[0], line)).EndsWith(Fmt(EvalFull(parts[1], line)), StringComparison.Ordinal);
    }

    static object EvalStrRepeat(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^повторить\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(t);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> повторить \"abc\" 3");
        string s = Fmt(EvalFull(parts[0], line));
        int n = (int)Math.Round(ToNum(EvalArith(parts[1], line), line));
        if (n < 0) n = 0;
        return string.Concat(Enumerable.Repeat(s, n));
    }

    static object EvalDictKeys(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^ключи\b", "", RegexOptions.IgnoreCase).Trim();
        if (!TryResolveVar(t, out var v) || v is not Dictionary<string, object> d)
            throw new Exception($"строка {line}: '{t}' не таблица");
        return new List<object>(d.Keys.Cast<object>());
    }

    static object EvalDictValues(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^значения\b", "", RegexOptions.IgnoreCase).Trim();
        if (!TryResolveVar(t, out var v) || v is not Dictionary<string, object> d)
            throw new Exception($"строка {line}: '{t}' не таблица");
        return new List<object>(d.Values);
    }

    static object EvalTypeOf(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^тип\b", "", RegexOptions.IgnoreCase).Trim();
        if (t == "") throw new Exception($"строка {line}: надо так -> тип переменная");
        object val = EvalFull(t, line);
        return val switch
        {
            int or double => "число",
            bool => "булево",
            string => "текст",
            List<object> => "список",
            Dictionary<string, object> => "таблица",
            _ => "неизвестно"
        };
    }

    static object EvalJsonParse(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^json\s+из\b", "", RegexOptions.IgnoreCase).Trim();
        if (t == "") t = Regex.Replace(expr.Trim(), @"^json\b", "", RegexOptions.IgnoreCase).Trim();
        string json = Fmt(EvalFull(t, line));
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return JsonElementToObject(doc.RootElement);
        }
        catch
        {
            throw new Exception($"строка {line}: не могу разобрать JSON: {json[..Math.Min(json.Length, 50)]}");
        }
    }

    static object JsonElementToObject(System.Text.Json.JsonElement el)
    {
        return el.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => el.GetString() ?? "",
            System.Text.Json.JsonValueKind.Number => el.TryGetInt32(out int iv) ? (object)iv : NormNum(el.GetDouble()),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => "",
            System.Text.Json.JsonValueKind.Array => new List<object>(el.EnumerateArray().Select(JsonElementToObject)),
            System.Text.Json.JsonValueKind.Object => el.EnumerateObject().Aggregate(
                new Dictionary<string, object>(),
                (d, p) => { d[p.Name] = JsonElementToObject(p.Value); return d; }),
            _ => ""
        };
    }

    static object EvalToJson(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^в\s+json\b", "", RegexOptions.IgnoreCase).Trim();
        if (t == "") throw new Exception($"строка {line}: надо так -> в json переменная");
        object val = EvalFull(t, line);
        return ObjectToJson(val);
    }

    static string ObjectToJson(object val)
    {
        if (val is bool b) return b ? "true" : "false";
        if (val is int i) return i.ToString();
        if (val is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (val is List<object> l) return "[" + string.Join(",", l.Select(ObjectToJson)) + "]";
        if (val is Dictionary<string, object> dict)
            return "{" + string.Join(",", dict.Select(kv => System.Text.Json.JsonSerializer.Serialize(kv.Key) + ":" + ObjectToJson(kv.Value))) + "}";
        return System.Text.Json.JsonSerializer.Serialize(Fmt(val));
    }

    static object EvalFileName(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^имя\s+файла\b", "", RegexOptions.IgnoreCase).Trim();
        return Path.GetFileName(Fmt(EvalFull(t, line)));
    }

    static object EvalFileExt(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^расширение\b", "", RegexOptions.IgnoreCase).Trim();
        return Path.GetExtension(Fmt(EvalFull(t, line)));
    }

    static object EvalFileDir(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^путь\s+к\s+папке\b", "", RegexOptions.IgnoreCase).Trim();
        return Path.GetDirectoryName(Fmt(EvalFull(t, line))) ?? "";
    }

    static bool TryResolveVar(string name, out object? v)
    {
        string lowName = name.ToLowerInvariant();
        if (lowName is "ширина_окна" or "окно_ширина") { v = GetCurrentWindowWidth(); return true; }
        if (lowName is "высота_окна" or "окно_высота") { v = GetCurrentWindowHeight(); return true; }
        lock (VarsLock)
        {
            if (Vars.TryGetValue(name, out v)) return true;
            if (name.Contains('.') && Vars.TryGetValue(name.Replace('.', '_'), out v)) return true;
        }
        if (name.Contains('_') || name.Contains('.'))
        {
            char sep = name.Contains('.') ? '.' : '_';
            int idx = name.IndexOf(sep);
            string objName = name[..idx];
            string prop = name[(idx + 1)..].ToLowerInvariant();
            lock (GameObjectsLock)
            {
                if (GameObjects.TryGetValue(objName, out var go))
                {
                    if (prop is "х" or "x") { v = go.X; return true; }
                    if (prop is "у" or "y") { v = go.Y; return true; }
                    if (prop is "размер" or "scale") { v = go.Scale; return true; }
                    if (prop is "прозрачность" or "альфа" or "alpha") { v = go.Alpha; return true; }
                    if (prop is "образ" or "sprite") { v = go.SpritePath; return true; }
                    if (prop is "тип_движения" or "motion") { v = go.Motion switch { ObjectMotion.Static => "статичный", ObjectMotion.Dynamic => "динамичный", _ => "нет" }; return true; }
                    if (prop is "скорость_х" or "vx") { v = go.VelocityX; return true; }
                    if (prop is "скорость_у" or "vy") { v = go.VelocityY; return true; }
                    if (prop is "скорость" or "speed") { v = go.Speed; return true; }
                    if (prop is "ускорение_х" or "ax") { v = go.AccelX; return true; }
                    if (prop is "ускорение_у" or "ay") { v = go.AccelY; return true; }
                    if (prop is "угол" or "angle") { v = go.Angle; return true; }
                    if (prop is "угловая_скорость" or "angular_velocity") { v = go.AngularVelocity; return true; }
                    if (prop is "масса" or "mass") { v = go.Mass; return true; }
                    if (prop is "демпфирование" or "damping") { v = go.Damping; return true; }
                    if (prop is "упругость" or "elasticity") { v = go.Elasticity; return true; }
                    if (prop is "трение" or "friction") { v = go.Friction; return true; }
                    if (prop is "лимит_скорости" or "max_speed") { v = go.MaxSpeed; return true; }
                    if (prop is "слой_коллизий" or "layer") { v = go.CollisionLayer; return true; }
                    if (go.Props.TryGetValue(prop, out v)) return true;
                }
            }
        }
        v = null;
        return false;
    }

    static object SingleTokValue(Tok t, int line)
    {
        if (t.Kind == TokKind.Num)
        {
            if (int.TryParse(t.Val, out int ii)) return ii;
            return double.Parse(t.Val, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (t.Kind == TokKind.Str) return t.Val;
        if (t.Kind == TokKind.Bool) return t.Val == "true";
        if (t.Kind == TokKind.Name)
        {
            if (!TryResolveVar(t.Val, out var v))
                throw new Exception($"строка {line}: нет такой переменной: {t.Val}");
            return v!;
        }
        throw new Exception($"строка {line}: не понимаю '{t.Val}'");
    }

    static int Prec(string op) => op == ".." ? 0 : op == "^" ? 3 : op is "*" or "/" or "%" ? 2 : 1;

    static List<Tok> ToRpn(List<Tok> toks, int line)
    {
        var out_ = new List<Tok>();
        var st = new Stack<Tok>();
        Tok? prev = null;
        foreach (var t in toks)
        {
            if (t.Kind is TokKind.Num or TokKind.Str or TokKind.Name or TokKind.Bool) { out_.Add(t); prev = t; }
            else if (t.Kind == TokKind.LPar) { st.Push(t); prev = t; }
            else if (t.Kind == TokKind.RPar)
            {
                bool found = false;
                while (st.Count > 0)
                {
                    var p = st.Pop();
                    if (p.Kind == TokKind.LPar) { found = true; break; }
                    out_.Add(p);
                }
                if (!found) throw new Exception($"строка {line}: лишняя ')'");
                prev = t;
            }
            else if (t.Kind == TokKind.Op)
            {
                string op = t.Val;
                if (op == "-" && (prev == null || prev.Kind == TokKind.Op || prev.Kind == TokKind.LPar))
                {
                    out_.Add(new Tok(TokKind.Num, "0"));
                }
                while (st.Count > 0 && st.Peek().Kind == TokKind.Op && (Prec(st.Peek().Val) > Prec(op) || (Prec(st.Peek().Val) == Prec(op) && op != ".." && op != "^")))
                    out_.Add(st.Pop());
                st.Push(t); prev = t;
            }
        }
        while (st.Count > 0)
        {
            var p = st.Pop();
            if (p.Kind == TokKind.LPar) throw new Exception($"строка {line}: нет ')'");
            out_.Add(p);
        }
        return out_;
    }

    static object EvalRpn(List<Tok> rpn, int line)
    {
        var st = new Stack<object>();
        foreach (var t in rpn)
        {
            if (t.Kind == TokKind.Op)
            {
                if (st.Count < 2) throw new Exception($"строка {line}: не хватает чисел для '{t.Val}'. Текст с пробелами надо в кавычках");
                var b = st.Pop(); var a = st.Pop();
                st.Push(ApplyOp(t.Val, a, b, line));
            }
            else st.Push(SingleTokValue(t, line));
        }
        if (st.Count != 1) throw new Exception($"строка {line}: два значения подряд без действия. Текст с пробелами надо в кавычках: \"...\"");
        return st.Pop();
    }

    static object ApplyOp(string op, object a, object b, int line)
    {
        if (op == "..") return Fmt(a) + Fmt(b);
        if (a is string || b is string)
        {
            if (op == "+") return Fmt(a) + Fmt(b);
            throw new Exception($"строка {line}: со строками можно только + или ..");
        }
        double x = ToNum(a, line), y = ToNum(b, line);
        return op switch
        {
            "+" => NormNum(x + y),
            "-" => NormNum(x - y),
            "*" => NormNum(x * y),
            "/" => y == 0 ? throw new Exception($"строка {line}: деление на ноль") : NormNum(x / y),
            "%" => y == 0 ? throw new Exception($"строка {line}: деление на ноль") : NormNum(x % y),
            "^" => NormNum(Math.Pow(x, y)),
            _ => throw new Exception($"строка {line}: не знаю действие '{op}'"),
        };
    }

    static readonly string[] CmpWords = { "больше или равно", "меньше или равно", "не равно", "неравно", "больше", "меньше", "равно" };
    static readonly string[] CmpSyms = { ">=", "<=", "!=", "<>", "==", ">", "<", "=" };

    static bool TryFindCmp(string expr, out string op, out string left, out string right)
    {
        op = ""; left = ""; right = "";
        bool inQ = false;
        int depth = 0;
        string low = expr.ToLowerInvariant();
        for (int i = 0; i < expr.Length; i++)
        {
            if (expr[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if (expr[i] == '(') { depth++; continue; }
            if (expr[i] == ')') { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            foreach (var w in CmpWords)
            {
                if (i + w.Length <= low.Length && low.Substring(i, w.Length) == w)
                {
                    bool lb = i == 0 || !(char.IsLetterOrDigit(low[i - 1]) || low[i - 1] == '_');
                    int e = i + w.Length;
                    bool rb = e >= low.Length || !(char.IsLetterOrDigit(low[e]) || low[e] == '_');
                    if (lb && rb)
                    {
                        op = w; left = expr[..i].Trim(); right = expr[e..].Trim();
                        return true;
                    }
                }
            }
            foreach (var s in CmpSyms)
            {
                if (i + s.Length <= expr.Length && expr.Substring(i, s.Length) == s)
                {
                    op = s; left = expr[..i].Trim(); right = expr[(i + s.Length)..].Trim();
                    return true;
                }
            }
        }
        return false;
    }

    static string NormCmp(string op)
    {
        op = op.ToLowerInvariant().Trim();
        if (op is "больше" or ">") return ">";
        if (op is "меньше" or "<") return "<";
        if (op is "равно" or "=" or "==") return "==";
        if (op is "не равно" or "неравно" or "!=" or "<>") return "!=";
        if (op is "больше или равно" or ">=") return ">=";
        if (op is "меньше или равно" or "<=") return "<=";
        return op;
    }

    static string StripOuterParens(string expr)
    {
        while (true)
        {
            expr = expr.Trim();
            if (expr.Length < 2 || expr[0] != '(') return expr;
            bool inQ = false;
            int depth = 0;
            bool closesAtEnd = false;
            for (int i = 0; i < expr.Length; i++)
            {
                if (expr[i] == '"') { inQ = !inQ; continue; }
                if (inQ) continue;
                if (expr[i] == '(') depth++;
                else if (expr[i] == ')')
                {
                    depth--;
                    if (depth == 0) { closesAtEnd = (i == expr.Length - 1); break; }
                }
            }
            if (!closesAtEnd) return expr;
            expr = expr[1..^1];
        }
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    static bool TrySplitLogic(string expr, out string op, out string left, out string right)
    {
        op = ""; left = ""; right = "";
        bool inQ = false;
        int depth = 0;
        string low = expr.ToLowerInvariant();
        for (int i = 0; i < expr.Length; i++)
        {
            if (expr[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if (expr[i] == '(') { depth++; continue; }
            if (expr[i] == ')') { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            if (i + 3 <= low.Length && low.Substring(i, 3) == "или")
            {
                bool lb = i == 0 || !IsWordChar(low[i - 1]);
                int e = i + 3;
                bool rb = e >= low.Length || !IsWordChar(low[e]);
                if (lb && rb)
                {
                    string l = expr[..i].Trim(), r = expr[e..].Trim();
                    if (l != "" && r != "") { op = "или"; left = l; right = r; return true; }
                }
            }
            if (i + 2 <= expr.Length && expr.Substring(i, 2) == "||")
            {
                string l = expr[..i].Trim(), r = expr[(i + 2)..].Trim();
                if (l != "" && r != "") { op = "или"; left = l; right = r; return true; }
            }
        }
        inQ = false; depth = 0;
        for (int i = 0; i < expr.Length; i++)
        {
            if (expr[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if (expr[i] == '(') { depth++; continue; }
            if (expr[i] == ')') { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            if (low[i] == 'и')
            {
                bool lb = i == 0 || !IsWordChar(low[i - 1]);
                bool rb = i + 1 >= low.Length || !IsWordChar(low[i + 1]);
                if (lb && rb)
                {
                    string l = expr[..i].Trim(), r = expr[(i + 1)..].Trim();
                    if (l != "" && r != "") { op = "и"; left = l; right = r; return true; }
                }
            }
            if (i + 2 <= expr.Length && expr.Substring(i, 2) == "&&")
            {
                string l = expr[..i].Trim(), r = expr[(i + 2)..].Trim();
                if (l != "" && r != "") { op = "и"; left = l; right = r; return true; }
            }
        }
        return false;
    }

    static bool TryStripNot(string expr, out string rest)
    {
        rest = "";
        string t = expr.Trim();
        string low = t.ToLowerInvariant();
        if (low.StartsWith("не") && (t.Length == 2 || !IsWordChar(t[2])))
        {
            string r = t[2..].Trim();
            if (r != "") { rest = r; return true; }
            return false;
        }
        if (t.StartsWith("!"))
        {
            string r = t[1..].Trim();
            if (r != "") { rest = r; return true; }
            return false;
        }
        return false;
    }

    static bool EvalCondition(string expr, int line)
    {
        expr = StripOuterParens(expr.Trim());
        if (expr == "") throw new Exception($"строка {line}: пустое условие");
        if (TrySplitLogic(expr, out string lop, out string ll, out string lr))
        {
            if (lop == "или") return EvalCondition(ll, line) || EvalCondition(lr, line);
            return EvalCondition(ll, line) && EvalCondition(lr, line);
        }
        if (TryStripNot(expr, out string nr)) return !EvalCondition(nr, line);
        if (TryFindCmp(expr, out string op, out string l, out string r))
        {
            if (l == "" || r == "") throw new Exception($"строка {line}: сравнение без краев: '{expr}'");
            object a = EvalArith(l, line), b = EvalArith(r, line);
            string n = NormCmp(op);
            bool aNum = a is int or double or bool, bNum = b is int or double or bool;
            if (aNum && bNum)
            {
                double x = ToNum(a, line), y = ToNum(b, line);
                return n switch
                {
                    ">" => x > y, "<" => x < y, "==" => Math.Abs(x - y) < 1e-9,
                    "!=" => Math.Abs(x - y) >= 1e-9, ">=" => x >= y - 1e-9, "<=" => x <= y + 1e-9,
                    _ => false
                };
            }
            string sa = Fmt(a), sb = Fmt(b);
            int c = string.Compare(sa, sb, StringComparison.Ordinal);
            return n switch
            {
                "==" => sa == sb, "!=" => sa != sb, ">" => c > 0, "<" => c < 0,
                ">=" => c >= 0, "<=" => c <= 0, _ => false
            };
        }
        return IsTrue(EvalArith(expr, line));
    }

    static object EvalFull(string expr, int line)
    {
        expr = StripOuterParens(expr.Trim());
        if (TryStripNot(expr, out _)) return EvalCondition(expr, line);
        if (TrySplitLogic(expr, out _, out _, out _)) return EvalCondition(expr, line);
        if (TryFindCmp(expr, out _, out _, out _)) return EvalCondition(expr, line);
        return EvalArith(expr, line);
    }

    static string ExtractWhileCond(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^пока\b", "", RegexOptions.IgnoreCase).Trim();
        t = StripTrailingWord(t, "то", "тогда", "делать");
        if (t == "") throw new Exception($"строка {line}: после 'пока' надо условие. Пример: пока счет < 10");
        return t;
    }
    static int EvalRepeatCount(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^повтори(ть|ять)?\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"[\s\t]+раз(а|ов)?[\s\t]*$", "", RegexOptions.IgnoreCase).Trim();
        if (t == "") throw new Exception($"строка {line}: надо так -> повтори 5 раз");
        object v = EvalArith(t, line);
        double d = ToNum(v, line);
        int n = (int)Math.Round(d);
        if (n < 0) throw new Exception($"строка {line}: повторить можно 0 и больше, а там {n}");
        return n;
    }
    static (string name, double from, double to, double step) ParseFor(string text, int line)
    {
        var m = Regex.Match(text.Trim(), @"^для\s+(\S+)\s+от\s+(.+?)\s+до\s+(.+?)(\s+шаг\s+(.+?))?(\s+то)?\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new Exception($"строка {line}: надо так -> для и от 1 до 10 то");
        string name = m.Groups[1].Value.Trim();
        if (!NameRegex.IsMatch(name)) throw new Exception($"строка {line}: плохое имя '{name}'");
        double a = ToNum(EvalArith(m.Groups[2].Value.Trim(), line), line);
        double b = ToNum(EvalArith(m.Groups[3].Value.Trim(), line), line);
        double s = m.Groups[5].Success ? ToNum(EvalArith(m.Groups[5].Value.Trim(), line), line) : (a <= b ? 1 : -1);
        if (Math.Abs(s) < 1e-12) throw new Exception($"строка {line}: шаг не может быть 0");
        return (name, a, b, s);
    }
    static (string name, List<object> items) ParseForEach(string text, int line)
    {
        var m = Regex.Match(text.Trim(), @"^для\s+каждого\s+(\S+)\s+в\s+(.+?)(\s+то)?\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new Exception($"строка {line}: надо так -> для каждого x в фрукты то");
        string name = m.Groups[1].Value.Trim();
        if (!NameRegex.IsMatch(name)) throw new Exception($"строка {line}: плохое имя '{name}'");
        object v = EvalFull(m.Groups[2].Value.Trim(), line);
        if (v is List<object> l) return (name, new List<object>(l));
        if (v is Dictionary<string, object> dict) return (name, new List<object>(dict.Keys.Cast<object>()));
        throw new Exception($"строка {line}: '{m.Groups[2].Value.Trim()}' не список и не таблица");
    }
    static void ExecShuffle(string text, int line)
    {
        string name = AfterFirstWord(text).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (!NameRegex.IsMatch(name)) throw new Exception($"строка {line}: надо так -> перемешать фрукты");
        if (!Vars.TryGetValue(name, out var v) || v is not List<object> l)
            throw new Exception($"строка {line}: нет такого списка: {name}");
        for (int i = l.Count - 1; i > 0; i--)
        {
            int j = Rnd.Next(i + 1);
            (l[i], l[j]) = (l[j], l[i]);
        }
    }
    static void ExecInsert(string text, int line)
    {
        string rest = AfterFirstWord(text);
        rest = Regex.Replace(rest, @"^в\s+", "", RegexOptions.IgnoreCase).Trim();
        var parts = rest.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) throw new Exception($"строка {line}: надо так -> вставить в фрукты 2 \"киви\"");
        if (!NameRegex.IsMatch(parts[0])) throw new Exception($"строка {line}: плохое имя списка '{parts[0]}'");
        if (!Vars.TryGetValue(parts[0], out var v) || v is not List<object> l)
            throw new Exception($"строка {line}: нет такого списка: {parts[0]}");
        object iv = EvalArith(parts[1], line);
        if (iv is string) throw new Exception($"строка {line}: место — число или переменная, а там текст");
        int idx = (int)Math.Round(ToNum(iv, line));
        if (idx < 1 || idx > l.Count + 1) throw new Exception($"строка {line}: в списке '{parts[0]}' всего {l.Count}, место от 1 до {l.Count + 1}");
        l.Insert(idx - 1, EvalFull(parts[2], line));
    }
    static void ExecAsk(string text, int line)
    {
        string rest = AfterFirstWord(text);
        int split = -1;
        bool inQ = false;
        for (int i = 0; i < rest.Length; i++)
        {
            if (rest[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if ((rest[i] == 'в' || rest[i] == 'В') && (i == 0 || !IsWordChar(rest[i - 1])) && (i + 1 >= rest.Length || !IsWordChar(rest[i + 1])))
                split = i;
        }
        if (split < 0) throw new Exception($"строка {line}: надо так -> спросить \"как зовут?\" в имя");
        string qExpr = rest[..split].Trim();
        string name = rest[(split + 1)..].Trim();
        if (qExpr == "" || !NameRegex.IsMatch(name))
            throw new Exception($"строка {line}: надо так -> спросить \"как зовут?\" в имя");
        object q = EvalFull(qExpr, line);
        Console.Write(Fmt(q));
        if (!Fmt(q).EndsWith(" ") && !Fmt(q).EndsWith("\n")) Console.Write(" ");
        string ans = Console.ReadLine() ?? "";
        Vars[name] = StoreValue(ans);
    }
    static readonly Dictionary<string, double> WaitUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        {"миллисекунду", 1}, {"миллисекунды", 1}, {"миллисекунд", 1}, {"мс", 1},
        {"секунду", 1000}, {"секунды", 1000}, {"секунд", 1000}, {"сек", 1000},
        {"минуту", 60000}, {"минуты", 60000}, {"минут", 60000}, {"мин", 60000},
        {"час", 3600000}, {"часа", 3600000}, {"часов", 3600000},
    };

    static void ExecWait(string text, int line)
    {
        string rest = AfterFirstWord(text).Trim();
        if (rest == "") throw new Exception($"строка {line}: надо так -> ждать 1 секунду");
        double mult = 1000;
        string numPart = rest;
        var m = Regex.Match(rest, @"^(.*?)[\s\t]+(\S+)\s*$");
        if (m.Success && WaitUnits.TryGetValue(m.Groups[2].Value, out double mm))
        {
            mult = mm;
            numPart = m.Groups[1].Value.Trim();
            if (numPart == "") throw new Exception($"строка {line}: надо так -> ждать 1 секунду");
        }
        double v = ToNum(EvalArith(numPart, line), line);
        if (v < 0) throw new Exception($"строка {line}: ждать можно 0 и больше");
        Thread.Sleep((int)Math.Round(v * mult));
    }
    static object StoreValue(string ans)
    {
        ans = ans.Trim().Trim((char)0xFEFF, (char)0x200B).Trim();
        if (int.TryParse(ans, out int ii)) return ii;
        if (double.TryParse(ans.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dd)) return NormNum(dd);
        return ans;
    }

    static int SplitFileTarget(string rest, int line, out string first, out string second)
    {
        int split = -1;
        bool inQ = false;
        for (int i = 0; i < rest.Length; i++)
        {
            if (rest[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if ((rest[i] == 'в' || rest[i] == 'В') && (i == 0 || !IsWordChar(rest[i - 1])) && (i + 1 >= rest.Length || !IsWordChar(rest[i + 1])))
                split = i;
        }
        first = ""; second = "";
        if (split < 0) return -1;
        first = rest[..split].Trim();
        second = rest[(split + 1)..].Trim();
        return split;
    }

    static void ExecWriteFile(string text, int line)
    {
        string rest = AfterFirstWord(text);
        if (SplitFileTarget(rest, line, out string valExpr, out string after) < 0)
            throw new Exception($"строка {line}: надо так -> записать \"привет\" в файл \"сейв.txt\"");
        string afterLow = after.ToLowerInvariant();
        if (!afterLow.StartsWith("файл") || (after.Length > 4 && IsWordChar(after[4])))
            throw new Exception($"строка {line}: надо так -> записать \"привет\" в файл \"сейв.txt\"");
        string pathExpr = after[4..].Trim();
        if (valExpr == "" || pathExpr == "")
            throw new Exception($"строка {line}: надо так -> записать \"привет\" в файл \"сейв.txt\"");
        object v = EvalFull(valExpr, line);
        string path = Fmt(EvalFull(pathExpr, line));
        if (System.IO.Path.GetExtension(path) == "") path += ".txt";
        try { File.WriteAllText(path, Fmt(v), new System.Text.UTF8Encoding(false)); }
        catch (Exception ex) { throw new Exception($"строка {line}: не записать '{path}': {ex.Message}"); }
    }

    static void ExecCreateWindow(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^создать\s+окно\b", "", RegexOptions.IgnoreCase).Trim();
        if (rest == "") throw new Exception($"строка {line}: надо так -> создать окно 800 600 \"Игра\"");
        var args = new List<string>();
        bool inQ = false;
        int s = 0;
        for (int i = 0; i < rest.Length; i++)
        {
            if (rest[i] == '"') inQ = !inQ;
            if (!inQ && rest[i] == ' ' || rest[i] == '\t')
            {
                if (i > s)
                {
                    var tok = rest[s..i].Trim();
                    if (tok != "") args.Add(tok);
                }
                s = i + 1;
                while (s < rest.Length && (rest[s] == ' ' || rest[s] == '\t')) s++;
                i = s - 1;
            }
        }
        var last = rest[s..].Trim();
        if (last != "") args.Add(last);
        if (args.Count < 3) throw new Exception($"строка {line}: надо так -> создать окно 800 600 \"Игра\"");
        int w = (int)Math.Round(ToNum(EvalFull(args[0], line), line));
        int h = (int)Math.Round(ToNum(EvalFull(args[1], line), line));
        string title = Fmt(EvalFull(args[2], line));
        if (w < 50 || w > 10000 || h < 50 || h > 10000) throw new Exception($"строка {line}: размер окна {w}x{h} странный");
        if (GameHostService.Current.IsActive)
        {
            EnsureGameWindow(w, h, title);
            Console.WriteLine($"[окно обновлено {w}x{h} \"{title}\"]");
            return;
        }
        EnsureGameWindow(w, h, title);
        Console.WriteLine($"[окно {w}x{h} \"{title}\"]");
    }

    class GameConfig
    {
        public string? Title { get; set; }
        public string? Main { get; set; }
        public bool? Resizable { get; set; }
        public bool? Fullscreen { get; set; }
        public string? Icon { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
    }
    static GameConfig? ActiveConfig;

    static void EnsureGameWindow(int w = 800, int h = 600, string title = "Ncode Game")
    {
        if (ActiveConfig != null)
        {
            if (!string.IsNullOrEmpty(ActiveConfig.Title)) title = ActiveConfig.Title;
            if (ActiveConfig.Width.HasValue && ActiveConfig.Width.Value > 0) w = ActiveConfig.Width.Value;
            if (ActiveConfig.Height.HasValue && ActiveConfig.Height.Value > 0) h = ActiveConfig.Height.Value;
        }
        WindowWidth = w;
        WindowHeight = h;
        lock (VarsLock)
        {
            Vars["ширина_окна"] = w;
            Vars["высота_окна"] = h;
            Vars["окно_ширина"] = w;
            Vars["окно_высота"] = h;
        }
        if (GameHostService.Current.IsActive) return;
        string? iconPath = null;
        if (!string.IsNullOrEmpty(ActiveConfig?.Icon))
        {
            iconPath = Path.Combine(curDir, ActiveConfig.Icon);
            if (!File.Exists(iconPath))
                iconPath = Path.Combine(AppContext.BaseDirectory, ActiveConfig.Icon);
        }
        GameHostService.Current.CreateWindow(w, h, title, ActiveConfig?.Resizable == true, ActiveConfig?.Fullscreen == true, iconPath);
    }

    static void InvalidateGameWindow()
    {
        GameHostService.Current.Invalidate();
    }

    static void TryLoadSpriteImage(GameObject obj)
    {
#if WINDOWS
        if (string.IsNullOrEmpty(obj.SpritePath)) return;
        try
        {
            string? resolved = null;
            var candidates = new[]
            {
                Path.Combine(curDir, obj.SpritePath),
                obj.SpritePath,
                Path.Combine(Environment.CurrentDirectory, obj.SpritePath)
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) { resolved = c; break; }
            }

            if (resolved != null)
            {
                using var stream = new FileStream(resolved, FileMode.Open, FileAccess.Read);
                obj.LoadedImage = Image.FromStream(stream);
            }
        }
        catch { }
#endif
    }

    static List<string> SplitArgsPreservingQuotes(string text)
    {
        var list = new List<string>();
        bool inQ = false;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') inQ = !inQ;
            else if (!inQ && (text[i] == ' ' || text[i] == '\t'))
            {
                if (i > start)
                {
                    var piece = text[start..i].Trim();
                    if (piece.Length > 0) list.Add(piece);
                }
                start = i + 1;
            }
        }
        if (start < text.Length)
        {
            var piece = text[start..].Trim();
            if (piece.Length > 0) list.Add(piece);
        }
        return list;
    }

    static string CleanStr(object val)
    {
        string s = Fmt(val);
        if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
            return s[1..^1];
        return s;
    }

    static void SyncObjectVars(string name)
    {
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(name, out var obj))
            {
                lock (VarsLock)
                {
                    Vars[$"{name}_х"] = obj.X;
                    Vars[$"{name}_у"] = obj.Y;
                    Vars[$"{name}_x"] = obj.X;
                    Vars[$"{name}_y"] = obj.Y;
                    Vars[$"{name}_размер"] = obj.Scale;
                    Vars[$"{name}_прозрачность"] = obj.Alpha;
                    Vars[$"{name}_образ"] = obj.SpritePath;
                    Vars[$"{name}_скорость_х"] = obj.VelocityX;
                    Vars[$"{name}_скорость_у"] = obj.VelocityY;
                    Vars[$"{name}_скорость"] = obj.Speed;
                    Vars[$"{name}_угол"] = obj.Angle;
                    Vars[$"{name}_масса"] = obj.Mass;
                    foreach (var kvp in obj.Props)
                    {
                        Vars[$"{name}_{kvp.Key}"] = kvp.Value;
                    }
                }
            }
        }
    }

    static void ExecDraw(string text, int line)
    {
        string rest = AfterFirstWord(text);
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 4) throw new Exception($"строка {line}: надо так -> нарисовать какашка icon.png 200 160 0");

        string name = parts[0];
        string sprite = CleanStr(EvalFull(parts[1], line));
        double x = ToNum(EvalFull(parts[2], line), line);
        double y = ToNum(EvalFull(parts[3], line), line);
        double alpha = parts.Count >= 5 ? ToNum(EvalFull(parts[4], line), line) : 0;

        EnsureGameWindow();

        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
            {
                GameObjects[name] = obj = new GameObject { Name = name };
            }
            obj.SpritePath = sprite;
            obj.X = x;
            obj.Y = y;
            obj.Alpha = alpha;
            obj.Visible = true;
            TryLoadSpriteImage(obj);
        }
        SyncObjectVars(name);
        InvalidateGameWindow();
    }

    static void ExecCreateObject(string text, int line)
    {
        string name = Regex.Replace(text.Trim(), @"^созда(ть|й)\s+(объект|обьект)\b", "", RegexOptions.IgnoreCase).Trim();
        if (name == "") throw new Exception($"строка {line}: укажите имя объекта, например: создать объект какашка");

        EnsureGameWindow();

        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
            {
                GameObjects[name] = new GameObject { Name = name };
            }
        }
        SyncObjectVars(name);
    }

    static void ExecAssignImage(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^присво(ить|й)\s+образ\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> присвоить образ icon.png какашка");

        string sprite = CleanStr(EvalFull(parts[0], line));
        string name = parts[1];

        EnsureGameWindow();

        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
            {
                GameObjects[name] = obj = new GameObject { Name = name };
            }
            obj.SpritePath = sprite;
            obj.Visible = true;
            TryLoadSpriteImage(obj);
        }

        SyncObjectVars(name);
        InvalidateGameWindow();
    }

    static void ExecAssignProp(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^присво(ить|й)\s+свойство\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 3) throw new Exception($"строка {line}: надо так -> присвоить свойство размер 60 какашка");

        string name = parts[^1];

        EnsureGameWindow();

        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
            {
                GameObjects[name] = obj = new GameObject { Name = name };
            }

            string p0 = parts[0].ToLowerInvariant();

            if ((p0 == "х" || p0 == "x") && parts.Count >= 5 && (parts[2].ToLowerInvariant() == "у" || parts[2].ToLowerInvariant() == "y"))
            {
                double targetX = ToNum(EvalFull(parts[1], line), line);
                double targetY = ToNum(EvalFull(parts[3], line), line);
                if (obj.Motion == ObjectMotion.Dynamic)
                    MoveDynamicObjectWithCollision(obj, targetX - obj.X, targetY - obj.Y);
                else
                {
                    obj.X = targetX;
                    obj.Y = targetY;
                }
            }
            else if (p0 == "х" || p0 == "x")
            {
                double targetX = ToNum(EvalFull(parts[1], line), line);
                if (obj.Motion == ObjectMotion.Dynamic)
                    MoveDynamicObjectWithCollision(obj, targetX - obj.X, 0);
                else
                    obj.X = targetX;
            }
            else if (p0 == "у" || p0 == "y")
            {
                double targetY = ToNum(EvalFull(parts[1], line), line);
                if (obj.Motion == ObjectMotion.Dynamic)
                    MoveDynamicObjectWithCollision(obj, 0, targetY - obj.Y);
                else
                    obj.Y = targetY;
            }
            else if (p0 is "тип_движения" or "движение")
            {
                string m = Fmt(EvalFull(parts[1], line)).ToLowerInvariant();
                if (m is "динамичный" or "динамический" or "dynamic")
                    obj.Motion = ObjectMotion.Dynamic;
                else if (m is "статичный" or "статический" or "static")
                    obj.Motion = ObjectMotion.Static;
                else
                    obj.Motion = ObjectMotion.None;
                obj.Props["тип_движения"] = m;
            }
            else if (p0 == "размер" || p0 == "масштаб" || p0 == "scale")
            {
                obj.Scale = ToNum(EvalFull(parts[1], line), line);
            }
            else if (p0 == "прозрачность" || p0 == "альфа" || p0 == "alpha")
            {
                obj.Alpha = ToNum(EvalFull(parts[1], line), line);
            }
            else
            {
                obj.Props[parts[0]] = EvalFull(parts[1], line);
            }
        }

        SyncObjectVars(name);
        InvalidateGameWindow();
    }

    static string ExtractKeyName(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^(когда\s+)?нажата\s+(клавиша|кнопка)\b", "", RegexOptions.IgnoreCase).Trim();
        if (t == "")
        {
            t = Regex.Replace(text.Trim(), @"^клавиша\s+нажата\b", "", RegexOptions.IgnoreCase).Trim();
        }
        if (t == "")
        {
            t = Regex.Replace(text.Trim(), @"^нажата\b", "", RegexOptions.IgnoreCase).Trim();
        }
        t = StripTrailingWord(t, "то", "тогда");
        var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new Exception($"строка {line}: укажите клавишу, например: нажата клавиша f");
        string rawKey = parts[0].Trim('"');
        return NormalizeKeyName(rawKey);
    }

    static string NormalizeKeyName(string k)
    {
        k = k.ToLowerInvariant().Trim();
        return k switch
        {
            "space" or "пробел" => "пробел",
            "up" or "вверх" or "стрелка_вверх" or "стрелкавверх" => "вверх",
            "down" or "вниз" or "стрелка_вниз" or "стрелкавниз" => "вниз",
            "left" or "влево" or "стрелка_влево" or "стрелкавлево" => "влево",
            "right" or "вправо" or "стрелка_вправо" or "стрелкавправо" => "вправо",
            "enter" or "ввод" => "ввод",
            "esc" or "escape" or "эскейп" => "эскейп",
            "ф" => "f",
            "ц" => "w",
            "ы" => "s",
            "в" => "d",
            "а" => "a",
            _ => k
        };
    }

#if WINDOWS
    static List<string> KeyToNames(Keys k)
    {
        var list = new List<string>();
        switch (k)
        {
            case Keys.Space:
                list.Add("пробел"); list.Add("space"); break;
            case Keys.Up:
                list.Add("вверх"); list.Add("стрелка_вверх"); list.Add("up"); break;
            case Keys.Down:
                list.Add("вниз"); list.Add("стрелка_вниз"); list.Add("down"); break;
            case Keys.Left:
                list.Add("влево"); list.Add("стрелка_влево"); list.Add("left"); break;
            case Keys.Right:
                list.Add("вправо"); list.Add("стрелка_вправо"); list.Add("right"); break;
            case Keys.Enter:
                list.Add("ввод"); list.Add("enter"); break;
            case Keys.Escape:
                list.Add("эскейп"); list.Add("escape"); break;
            default:
                string name = k.ToString().ToLowerInvariant();
                list.Add(name);
                var ru = EngToRuKey(name);
                if (ru != null) list.Add(ru);
                break;
        }
        return list;
    }
#endif

    static string? EngToRuKey(string eng) => eng switch
    {
        "q" => "й", "w" => "ц", "e" => "у", "r" => "к", "t" => "е", "y" => "н",
        "u" => "г", "i" => "ш", "o" => "щ", "p" => "з", "a" => "ф", "s" => "ы",
        "d" => "в", "f" => "а", "g" => "п", "h" => "р", "j" => "о", "k" => "л",
        "l" => "д", "z" => "я", "x" => "ч", "c" => "с", "v" => "м", "b" => "и",
        "n" => "т", "m" => "ь", _ => null
    };

    class ClickHandler
    {
        public string Target = "";
        public List<Line> Body = new();
    }
    static readonly List<ClickHandler> ClickHandlers = new();

    static string ExtractClickTarget(string text)
    {
        string t = Regex.Replace(text.Trim(), @"^при\s+нажати(и|ях?)\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^на\s+", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^(объект|обьект)\s+", "", RegexOptions.IgnoreCase).Trim();
        t = StripTrailingWord(t, "то", "тогда");
        return t.Trim('"', ' ');
    }

    static void HandleGameClick(double x, double y)
    {
        lock (VarsLock)
        {
            Vars["нажатие_х"] = NormNum(x);
            Vars["нажатие_у"] = NormNum(y);
            Vars["мышь_х"] = NormNum(x);
            Vars["мышь_у"] = NormNum(y);
            Vars["мышь_x"] = NormNum(x);
            Vars["мышь_y"] = NormNum(y);
        }

        List<string> hitObjects = new();
        lock (GameObjectsLock)
        {
            foreach (var kv in GameObjects)
            {
                if (kv.Value.ContainsPoint(x, y))
                    hitObjects.Add(kv.Key);
            }
        }

        if (hitObjects.Count > 0)
        {
            lock (VarsLock)
            {
                Vars["нажатый_объект"] = hitObjects[^1];
                Vars["нажатый_обьект"] = hitObjects[^1];
            }
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                List<ClickHandler> handlersToRun = new();
                lock (ClickHandlers)
                {
                    foreach (var h in ClickHandlers)
                    {
                        if (string.IsNullOrEmpty(h.Target))
                        {
                            handlersToRun.Add(h);
                        }
                        else if (hitObjects.Any(name => name.Equals(h.Target, StringComparison.OrdinalIgnoreCase)))
                        {
                            handlersToRun.Add(h);
                        }
                    }
                }
                foreach (var h in handlersToRun)
                {
                    ExecRange(h.Body, 0, h.Body.Count);
                }
                InvalidateGameWindow();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ошибка нажатия] " + ex.Message);
            }
        });
    }

    static void HandleGameKeyDown(string rawKey)
    {
        var keyNames = KeyToNames(rawKey);
        lock (VarsLock)
        {
            Vars["нажатая_клавиша"] = keyNames.FirstOrDefault() ?? "";
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                foreach (var kn in keyNames)
                {
                    var norm = NormalizeKeyName(kn);
                    DispatchKey(norm);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ошибка клавиши] " + ex.Message);
            }
        });
    }

    static List<string> KeyToNames(string rawKey)
    {
        var list = new List<string>();
        string low = rawKey.ToLowerInvariant().Trim();
        list.Add(low);
        var ru = EngToRuKey(low);
        if (ru != null) list.Add(ru);
        return list;
    }

    static void DispatchKey(string key)
    {
        key = key.ToLowerInvariant().Trim();
        bool ran = false;
        if (KeyHandlers.TryGetValue(key, out var list))
        {
            foreach (var body in list)
            {
                ExecRange(body, 0, body.Count);
                ran = true;
            }
        }
        if (KeyHandlers.TryGetValue("любая", out var anyList))
        {
            foreach (var body in anyList)
            {
                ExecRange(body, 0, body.Count);
                ran = true;
            }
        }
        if (ran)
        {
            InvalidateGameWindow();
        }
    }

    static void CollectKeyHandlers(List<Line> code)
    {
        for (int i = 0; i < code.Count; i++)
        {
            if (IsKeyHandler(code[i].Text))
            {
                string key = ExtractKeyName(code[i].Text, code[i].No);
                var (end, _) = FindBlock(code, i);
                var body = code.GetRange(i + 1, end - i - 1);
                if (!KeyHandlers.TryGetValue(key, out var list)) KeyHandlers[key] = list = new();
                list.Add(body);
                i = end;
            }
            else if (IsOnClick(code[i].Text))
            {
                string target = ExtractClickTarget(code[i].Text);
                var (end, _) = FindBlock(code, i);
                var body = code.GetRange(i + 1, end - i - 1);
                lock (ClickHandlers) { ClickHandlers.Add(new ClickHandler { Target = target, Body = body }); }
                i = end;
            }
        }
    }

    static void ExecRunScene(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^запустить\s+сцену\b", "", RegexOptions.IgnoreCase).Trim();
        if (rest == "") throw new Exception($"строка {line}: надо так -> запустить сцену \"game.ncode\"");
        var parts = new List<string>();
        bool inQ = false;
        int start = 0;
        for (int i = 0; i < rest.Length; i++)
        {
            if (rest[i] == '"') inQ = !inQ;
            if (!inQ && rest[i] == ' ')
            {
                var tok = rest[start..i].Trim();
                if (tok != "") parts.Add(tok);
                start = i + 1;
            }
        }
        var last = rest[start..].Trim();
        if (last != "") parts.Add(last);
        if (parts.Count == 0) throw new Exception($"строка {line}: надо так -> запустить сцену \"game.ncode\"");
        string path = Fmt(EvalFull(parts[0], line));
        bool clear = true;
        bool stopOld = true;
        if (parts.Count >= 2)
        {
            object v = EvalFull(parts[1], line);
            if (v is bool b) clear = b;
            else if (v is string s && bool.TryParse(s, out bool pb)) clear = pb;
            else clear = IsTrue(v);
        }
        if (parts.Count >= 3)
        {
            object v = EvalFull(parts[2], line);
            if (v is bool b) stopOld = b;
            else if (v is string s && bool.TryParse(s, out bool pb)) stopOld = pb;
            else stopOld = IsTrue(v);
        }
        if (clear && GameHostService.Current.IsActive)
        {
            GameHostService.Current.Invalidate();
            Console.WriteLine("[окно очищено]");
        }
        else if (clear) Console.WriteLine("[очистка сцены]");
        if (stopOld) Console.WriteLine($"[стоп старой сцены]");
        Console.WriteLine($"[запуск сцены {path} очистка={Fmt(clear)} стоп={Fmt(stopOld)}]");
        string real = "";
        var candidates = new[] { Path.Combine(curDir, path), path, Path.Combine(Environment.CurrentDirectory, path) };
        foreach (var cand in candidates)
        {
            if (File.Exists(cand)) { real = cand; break; }
            if (File.Exists(cand + ".ncode")) { real = cand + ".ncode"; break; }
        }
        if (real == "" ) real = path;
        if (!File.Exists(real) && File.Exists(real + ".ncode")) real += ".ncode";
        if (!File.Exists(real))
        {
            throw new Exception($"строка {line}: нет сцены '{path}'");
        }
        var code = LoadWithIncludes(real, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var sceneHandlers = new List<(string ev, List<Line> body)>();
        var sceneTriggers = new List<Trigger>();
        for (int ci = 0; ci < code.Count; ci++)
        {
            if (IsWhen(code[ci].Text))
            {
                string ev = ExtractEventDef(code[ci].Text);
                var (end, _) = FindBlock(code, ci);
                var body = code.GetRange(ci + 1, end - ci - 1);
                sceneHandlers.Add((ev, body));
                ci = end;
            }
            else if (IsTrigger(code[ci].Text))
            {
                string c = CondFromTriggerLine(code[ci].Text, code[ci].No);
                var (end, _) = FindBlock(code, ci);
                var body = code.GetRange(ci + 1, end - ci - 1);
                sceneTriggers.Add(new Trigger { Cond = c, Body = body, No = code[ci].No });
                ci = end;
            }
            else if (IsOnStart(code[ci].Text))
            {
                var (end, _) = FindBlock(code, ci);
                var body = code.GetRange(ci + 1, end - ci - 1);
                try { ExecRange(body, 0, body.Count); } catch (Exception ex) when (ex.Message.StartsWith("строка")) { throw; }
                ci = end;
            }
        }
        foreach (var (ev, body) in sceneHandlers)
        {
            if (!Handlers.TryGetValue(ev, out var list)) Handlers[ev] = list = new();
            list.Add(body);
        }
        foreach (var tr in sceneTriggers) Triggers.Add(tr);
        try { ExecRange(code, 0, code.Count); }
        catch (Exception ex) when (!ex.Message.StartsWith("строка")) { throw new Exception($"строка {line}: сцена '{path}': {ex.Message}"); }
    }

    static string ExtractScriptName(string text, string verb, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^" + Regex.Escape(verb) + @"\s+", "", RegexOptions.IgnoreCase).Trim();
        rest = Regex.Replace(rest, @"^скрипт\s+", "", RegexOptions.IgnoreCase).Trim();
        rest = rest.Trim('"');
        if (rest == "") throw new Exception($"строка {line}: укажи имя скрипта, например: {verb} enemy.ncode");
        return rest;
    }

    static void ExecRunScript(string text, int line)
    {
        string path = ExtractScriptName(text, "запустить", line);
        path = Fmt(EvalFull(path, line));
        if (System.IO.Path.GetExtension(path) == "") path += ".ncode";
        string real = "";
        var candidates = new[] { System.IO.Path.Combine(curDir, path), path, System.IO.Path.Combine(Environment.CurrentDirectory, path) };
        foreach (var cand in candidates)
        {
            if (File.Exists(cand)) { real = cand; break; }
        }
        if (real == "") throw new Exception($"строка {line}: нет скрипта '{path}'");
        string scriptPath = real;
        var t = new Thread(() =>
        {
            try
            {
                var code = LoadWithIncludes(scriptPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                CollectKeyHandlers(code);
                ExecRange(code, 0, code.Count);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[скрипт {System.IO.Path.GetFileName(scriptPath)} ошибка] {ex.Message}");
            }
            finally
            {
                lock (ScriptThreadsLock)
                {
                    ScriptThreads.Remove(Thread.CurrentThread);
                }
            }
        });
        t.IsBackground = true;
        t.Name = $"ncode:{System.IO.Path.GetFileName(scriptPath)}";
        lock (ScriptThreadsLock) { ScriptThreads.Add(t); }
        t.Start();
        Console.WriteLine($"[запущен скрипт {System.IO.Path.GetFileName(scriptPath)}]");
    }

    static void ExecStopScripts()
    {
        List<Thread> toStop;
        lock (ScriptThreadsLock)
        {
            toStop = new List<Thread>(ScriptThreads);
            ScriptThreads.Clear();
        }
        foreach (var t in toStop)
        {
            try { t.Interrupt(); } catch { }
        }
        Console.WriteLine($"[остановлено скриптов: {toStop.Count}]");
    }

    static void ExecStopScript(string text, int line)
    {
        string name = ExtractScriptName(text, "остановить", line);
        name = Fmt(EvalFull(name, line));
        if (System.IO.Path.GetExtension(name) == "") name += ".ncode";
        string threadName = $"ncode:{name}";
        List<Thread> toStop;
        lock (ScriptThreadsLock)
        {
            toStop = ScriptThreads.Where(t => t.Name == threadName).ToList();
            foreach (var t in toStop) ScriptThreads.Remove(t);
        }
        if (toStop.Count == 0)
        {
            Console.WriteLine($"[скрипт '{name}' не запущен]");
            return;
        }
        foreach (var t in toStop)
        {
            try { t.Interrupt(); } catch { }
        }
        Console.WriteLine($"[остановлен скрипт '{name}']");
    }

    static string GetCurrentFileDir()
    {
        return curDir;
    }

    static void ExecReadFile(string text, int line)
    {
        string rest = AfterFirstWord(text);
        string low = rest.ToLowerInvariant();
        if (!low.StartsWith("файл") || (rest.Length > 4 && IsWordChar(rest[4])))
            throw new Exception($"строка {line}: надо так -> прочитать файл \"сейв.txt\" в данные");
        string after = rest[4..].Trim();
        if (SplitFileTarget(after, line, out string pathExpr, out string name) < 0)
            throw new Exception($"строка {line}: надо так -> прочитать файл \"сейв.txt\" в данные");
        if (pathExpr == "" || !NameRegex.IsMatch(name))
            throw new Exception($"строка {line}: надо так -> прочитать файл \"сейв.txt\" в данные");
        string path = Fmt(EvalFull(pathExpr, line));
        if (System.IO.Path.GetExtension(path) == "") path += ".txt";
        string content;
        try { content = File.ReadAllText(path, System.Text.Encoding.UTF8); }
        catch (Exception ex) { throw new Exception($"строка {line}: не прочитать '{path}': {ex.Message}"); }
        Vars[name] = StoreValue(content);
    }

    static void ExecDelete(string text, int line)
    {
        string rest = AfterFirstWord(text);
        rest = Regex.Replace(rest, @"^из\s+", "", RegexOptions.IgnoreCase).Trim();
        int sp = rest.IndexOfAny(new[] { ' ', '\t' });
        if (sp < 0) throw new Exception($"строка {line}: надо так -> удалить из фрукты 1");
        string name = rest[..sp].Trim();
        string idxExpr = rest[(sp + 1)..].Trim();
        if (!NameRegex.IsMatch(name) || idxExpr == "")
            throw new Exception($"строка {line}: надо так -> удалить из {name} 1");
        if (!Vars.TryGetValue(name, out var v) || v is not List<object> l)
            throw new Exception($"строка {line}: нет такого списка: {name}");
        int idx = (int)Math.Round(ToNum(EvalArith(idxExpr, line), line));
        if (idx < 1 || idx > l.Count) throw new Exception($"строка {line}: в списке '{name}' всего {l.Count}, а просят {idx} (счет с 1)");
        l.RemoveAt(idx - 1);
    }

    static void ExecClear(string text, int line)
    {
        string name = AfterFirstWord(text).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (!NameRegex.IsMatch(name))
            throw new Exception($"строка {line}: надо так -> очистить фрукты");
        if (!Vars.TryGetValue(name, out var v) || v is not List<object> l)
            throw new Exception($"строка {line}: нет такого списка: {name}");
        l.Clear();
    }

    static double ParseEverySec(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^каждые\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^каждый\b", "", RegexOptions.IgnoreCase).Trim();
        if (t == "") throw new Exception($"строка {line}: надо так -> каждые 2 секунды");
        double mult = 1;
        var m = Regex.Match(t, @"^(.*?)[\s\t]+(\S+)\s*$");
        string numPart = t;
        if (m.Success && WaitUnits.TryGetValue(m.Groups[2].Value, out double mm))
        {
            mult = mm / 1000.0;
            numPart = m.Groups[1].Value.Trim();
            if (numPart == "") throw new Exception($"строка {line}: надо так -> каждые 2 секунды");
        }
        double v = ToNum(EvalArith(numPart, line), line);
        if (v <= 0) throw new Exception($"строка {line}: время больше 0");
        return v * mult;
    }

    static void ExecSplit(string text, int line)
    {
        var m = Regex.Match(text.Trim(), @"^разделить\s+(.+?)\s+по\s+(.+?)\s+в\s+(\S+)\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new Exception($"строка {line}: надо так -> разделить \"а,б\" по \",\" в части");
        string src = Fmt(EvalFull(m.Groups[1].Value.Trim(), line));
        string sep = Fmt(EvalFull(m.Groups[2].Value.Trim(), line));
        string name = m.Groups[3].Value.Trim();
        if (!NameRegex.IsMatch(name)) throw new Exception($"строка {line}: плохое имя '{name}'");
        var parts = sep == "" ? src.Select(c => c.ToString()).ToArray() : src.Split(sep);
        Vars[name] = new List<object>(parts.Cast<object>());
    }

    static void ExecJoin(string text, int line)
    {
        var m = Regex.Match(text.Trim(), @"^склеить\s+(\S+)\s+по\s+(.+?)\s+в\s+(\S+)\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new Exception($"строка {line}: надо так -> склеить части по \",\" в текст");
        string listName = m.Groups[1].Value.Trim();
        string sep = Fmt(EvalFull(m.Groups[2].Value.Trim(), line));
        string dst = m.Groups[3].Value.Trim();
        if (!NameRegex.IsMatch(listName) || !NameRegex.IsMatch(dst))
            throw new Exception($"строка {line}: надо так -> склеить {listName} по \",\" в {dst}");
        if (!Vars.TryGetValue(listName, out var v) || v is not List<object> l)
            throw new Exception($"строка {line}: нет такого списка: {listName}");
        Vars[dst] = string.Join(sep, l.Select(Fmt));
    }

    static void ExecFileExists(string text, int line)
    {
        string arg = AfterFirstWord(text).Trim();
        if (arg.ToLowerInvariant().StartsWith("файл"))
            arg = arg[4..].Trim();
        if (arg == "") throw new Exception($"строка {line}: надо так -> есть файл \"сейв.txt\"");
    }

    static bool EvalFileExists(string expr, int line)
    {
        string t = Regex.Replace(expr.Trim(), @"^есть\s+файл\b", "", RegexOptions.IgnoreCase).Trim();
        if (t == "") throw new Exception($"строка {line}: надо так -> есть файл \"сейв.txt\"");
        string path = Fmt(EvalFull(t, line));
        if (System.IO.Path.GetExtension(path) == "") path += ".txt";
        return File.Exists(path) || Directory.Exists(path);
    }

    static void ExecCreateFolder(string text, int line)
    {
        string arg = Regex.Replace(text.Trim(), @"^создать\s+папку\b", "", RegexOptions.IgnoreCase).Trim();
        if (arg == "") throw new Exception($"строка {line}: надо так -> создать папку \"моисевы\"");
        string path = Fmt(EvalFull(arg, line));
        try { Directory.CreateDirectory(path); }
        catch (Exception ex) { throw new Exception($"строка {line}: не создать папку '{path}': {ex.Message}"); }
    }

    static void StopAllAudio()
    {
        try { AudioService.Current.StopAll(); } catch { }
    }

    static void ExecPlaySound(string text, int line)
    {
        bool waitForEnd = Regex.IsMatch(text.Trim(), @"\bи\s+(ждать|жди)\s*$", RegexOptions.IgnoreCase);

        string rest = Regex.Replace(text.Trim(), @"^(воспроизвести|воспроизведи|играть|играй|сыграть|сыграй)(\s+звук)?\s*", "", RegexOptions.IgnoreCase).Trim();
        if (waitForEnd)
        {
            rest = Regex.Replace(rest, @"\bи\s+(ждать|жди)\s*$", "", RegexOptions.IgnoreCase).Trim();
        }

        if (string.IsNullOrWhiteSpace(rest))
            throw new Exception($"строка {line}: укажите файл звука. Пример: воспроизвести звук \"game.mp3\"");

        string raw = rest.Trim();
        string soundFile;
        if ((raw.StartsWith("\"") && raw.EndsWith("\"")) || TryResolveVar(raw, out _))
        {
            soundFile = CleanStr(EvalFull(raw, line));
        }
        else
        {
            try
            {
                soundFile = CleanStr(EvalFull(raw, line));
            }
            catch
            {
                soundFile = raw.Trim('"');
            }
        }

        if (string.IsNullOrWhiteSpace(soundFile))
            throw new Exception($"строка {line}: пустое имя файла звука");

        string real = "";
        var candidates = new List<string>
        {
            soundFile,
            Path.Combine(curDir, soundFile),
            Path.Combine(Environment.CurrentDirectory, soundFile),
            Path.Combine(AppContext.BaseDirectory, soundFile)
        };

        if (string.IsNullOrEmpty(Path.GetExtension(soundFile)))
        {
            foreach (var ext in new[] { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac" })
            {
                candidates.Add(soundFile + ext);
                candidates.Add(Path.Combine(curDir, soundFile + ext));
                candidates.Add(Path.Combine(Environment.CurrentDirectory, soundFile + ext));
                candidates.Add(Path.Combine(AppContext.BaseDirectory, soundFile + ext));
            }
        }

        foreach (var cand in candidates)
        {
            if (File.Exists(cand)) { real = Path.GetFullPath(cand); break; }
        }

        if (string.IsNullOrEmpty(real))
            throw new Exception($"строка {line}: файл звука не найден: '{soundFile}'");

        try
        {
            AudioService.Current.Play(real, waitForEnd);
        }
        catch (Exception ex) when (!ex.Message.StartsWith("строка"))
        {
            throw new Exception($"строка {line}: ошибка воспроизведения звука: {ex.Message}");
        }
    }

    static string ResolveSoundPath(string soundFile)
    {
        var candidates = new List<string>
        {
            soundFile,
            Path.Combine(curDir, soundFile),
            Path.Combine(Environment.CurrentDirectory, soundFile),
            Path.Combine(AppContext.BaseDirectory, soundFile)
        };

        if (string.IsNullOrEmpty(Path.GetExtension(soundFile)))
        {
            foreach (var ext in new[] { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac" })
            {
                candidates.Add(soundFile + ext);
                candidates.Add(Path.Combine(curDir, soundFile + ext));
                candidates.Add(Path.Combine(Environment.CurrentDirectory, soundFile + ext));
                candidates.Add(Path.Combine(AppContext.BaseDirectory, soundFile + ext));
            }
        }

        foreach (var cand in candidates)
        {
            if (File.Exists(cand)) return Path.GetFullPath(cand);
        }
        return soundFile;
    }

    static void ExecStopSound(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^(остановить|останови|стоп)\s+звук\s*", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(rest))
        {
            AudioService.Current.Stop(null);
            return;
        }
        string soundFile = CleanStr(EvalFull(rest, line));
        string path = ResolveSoundPath(soundFile);
        AudioService.Current.Stop(path);
    }

    static void ExecPauseSound(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^(пауза\s+звук|пауза|приостановить\s+звук)\s*", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(rest))
        {
            AudioService.Current.Pause(null);
            return;
        }
        string soundFile = CleanStr(EvalFull(rest, line));
        string path = ResolveSoundPath(soundFile);
        AudioService.Current.Pause(path);
    }

    static void ExecResumeSound(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^(продолжить|возобновить|возобнови)\s+звук\s*", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(rest))
        {
            AudioService.Current.Resume(null);
            return;
        }
        string soundFile = CleanStr(EvalFull(rest, line));
        string path = ResolveSoundPath(soundFile);
        AudioService.Current.Resume(path);
    }

    static void ExecSetVolume(string text, int line)
    {
        string rest = AfterFirstWord(text).Trim();
        if (string.IsNullOrWhiteSpace(rest))
            throw new Exception($"строка {line}: укажите громкость от 0 до 100");
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count == 1)
        {
            int vol = (int)Math.Round(ToNum(EvalArith(parts[0], line), line));
            AudioService.Current.SetVolume(null, vol);
        }
        else
        {
            string soundFile = CleanStr(EvalFull(parts[0], line));
            int vol = (int)Math.Round(ToNum(EvalArith(parts[1], line), line));
            string path = ResolveSoundPath(soundFile);
            AudioService.Current.SetVolume(path, vol);
        }
    }

    static void ExecShowMessage(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^показ(ать|и)\s+сообщение\s*", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(rest))
            throw new Exception($"строка {line}: укажите текст сообщения");
        DialogService.Current.ShowMessage(Fmt(EvalFull(rest, line)));
    }

    static void ExecShowError(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^показ(ать|и)\s+ошибку\s*", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(rest))
            throw new Exception($"строка {line}: укажите текст ошибки");
        DialogService.Current.ShowError(Fmt(EvalFull(rest, line)));
    }

    static bool SplitTargetVar(string text, out string expr, out string varName)
    {
        expr = "";
        varName = "";
        int arrowIdx = -1;
        int arrowLen = 1;
        bool inQ = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if (text[i] == '→')
            {
                arrowIdx = i;
                arrowLen = 1;
            }
            else if (i + 1 < text.Length && text[i] == '-' && text[i + 1] == '>')
            {
                arrowIdx = i;
                arrowLen = 2;
            }
        }
        if (arrowIdx >= 0)
        {
            expr = text[..arrowIdx].Trim();
            varName = text[(arrowIdx + arrowLen)..].Trim();
            return true;
        }
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if ((text[i] == 'в' || text[i] == 'В') && (i == 0 || !IsWordChar(text[i - 1])) && (i + 1 >= text.Length || !IsWordChar(text[i + 1])))
            {
                string potentialVar = text[(i + 1)..].Trim();
                if (NameRegex.IsMatch(potentialVar))
                {
                    expr = text[..i].Trim();
                    varName = potentialVar;
                    return true;
                }
            }
        }
        return false;
    }

    static void ExecAskYesNo(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^спрос(ить|и)\s+(да\s+или\s+нет|да\/нет)\s*", "", RegexOptions.IgnoreCase).Trim();
        string qExpr = rest;
        string varName = "";
        if (SplitTargetVar(rest, out string e, out string v))
        {
            qExpr = e;
            varName = v;
        }
        if (string.IsNullOrWhiteSpace(qExpr))
            throw new Exception($"строка {line}: укажите вопрос. Пример: спросить да или нет \"Ты уверен?\" → ответ");
        bool answer = DialogService.Current.AskYesNo(Fmt(EvalFull(qExpr, line)));
        if (!string.IsNullOrWhiteSpace(varName))
        {
            if (!NameRegex.IsMatch(varName))
                throw new Exception($"строка {line}: плохое имя переменной '{varName}'");
            Vars[varName] = answer ? "да" : "нет";
        }
    }

    static void ExecCopyClipboard(string text, int line)
    {
        string rest = AfterFirstWord(text).Trim();
        if (string.IsNullOrWhiteSpace(rest))
            throw new Exception($"строка {line}: что скопировать? Пример: скопировать \"текст\"");
        ClipboardService.Current.Copy(Fmt(EvalFull(rest, line)));
    }

    static void ExecPasteClipboard(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^вставить\s*(из\s+буфера)?\s*", "", RegexOptions.IgnoreCase).Trim();
        string varName = "";
        if (rest.StartsWith("→") || rest.StartsWith("->"))
        {
            varName = rest.TrimStart('→', '-', '>').Trim();
        }
        else if (rest.ToLowerInvariant().StartsWith("в ") || rest.ToLowerInvariant().StartsWith("в\t"))
        {
            varName = rest[2..].Trim();
        }
        else
        {
            varName = rest.Trim();
        }
        if (string.IsNullOrWhiteSpace(varName) || !NameRegex.IsMatch(varName))
            throw new Exception($"строка {line}: укажите переменную для вставки. Пример: вставить → переменная");
        Vars[varName] = ClipboardService.Current.Paste();
    }

    static void ExecHttpGet(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^получить\s*", "", RegexOptions.IgnoreCase).Trim();
        if (!SplitTargetVar(rest, out string urlExpr, out string varName) || string.IsNullOrWhiteSpace(varName))
            throw new Exception($"строка {line}: надо так -> получить \"https://...\" → переменная");
        if (!NameRegex.IsMatch(varName))
            throw new Exception($"строка {line}: плохое имя переменной '{varName}'");
        string url = CleanStr(EvalFull(urlExpr, line));
        try
        {
            string resp = Http.GetStringAsync(url).GetAwaiter().GetResult();
            SetVar(varName, StoreValue(resp));
        }
        catch (Exception ex)
        {
            throw new Exception($"строка {line}: ошибка сети GET '{url}': {ex.Message}");
        }
    }

    static void ExecHttpSend(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^отправить\s*", "", RegexOptions.IgnoreCase).Trim();
        int sp = rest.IndexOfAny(new[] { ' ', '\t' });
        if (sp < 0) throw new Exception($"строка {line}: укажите метод HTTP. Пример: отправить POST \"https://...\" \"тело\" → ответ");
        string method = rest[..sp].Trim().ToUpperInvariant();
        string afterMethod = rest[(sp + 1)..].Trim();
        string beforeArrow = afterMethod;
        string varName = "";
        if (SplitTargetVar(afterMethod, out string bArrow, out string v))
        {
            beforeArrow = bArrow;
            varName = v;
        }
        var parts = SplitArgsPreservingQuotes(beforeArrow);
        if (parts.Count == 0) throw new Exception($"строка {line}: укажите адрес URL");
        string url = CleanStr(EvalFull(parts[0], line));
        string body = parts.Count > 1 ? Fmt(EvalFull(parts[1], line)) : "";
        try
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), url);
            if (method != "GET" && method != "DELETE" && !string.IsNullOrEmpty(body))
            {
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            using var resp = Http.SendAsync(req).GetAwaiter().GetResult();
            string content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(varName))
            {
                if (!NameRegex.IsMatch(varName)) throw new Exception($"строка {line}: плохое имя переменной '{varName}'");
                SetVar(varName, StoreValue(content));
            }
        }
        catch (Exception ex) when (!ex.Message.StartsWith("строка"))
        {
            throw new Exception($"строка {line}: ошибка HTTP {method} '{url}': {ex.Message}");
        }
    }

    static string ToFirebaseJson(object val)
    {
        if (val is bool b) return b ? "true" : "false";
        if (val is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (val is int i) return i.ToString();
        string s = Fmt(val);
        if (s.Equals("истина", StringComparison.OrdinalIgnoreCase)) return "true";
        if (s.Equals("ложь", StringComparison.OrdinalIgnoreCase)) return "false";
        if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double num))
            return num.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string st = s.Trim();
        if ((st.StartsWith("{") && st.EndsWith("}")) || (st.StartsWith("[") && st.EndsWith("]")))
            return st;
        return System.Text.Json.JsonSerializer.Serialize(s);
    }

    static object FromFirebaseJson(string json)
    {
        string t = json.Trim();
        if (t == "null") return "";
        if (t == "true") return true;
        if (t == "false") return false;
        if (t.StartsWith("\"") && t.EndsWith("\"") && t.Length >= 2)
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<string>(t) ?? ""; }
            catch { return t[1..^1]; }
        }
        return StoreValue(t);
    }

    static void ExecFirebaseWrite(string text, int line)
    {
        string rest = AfterFirstWord(text).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 3)
            throw new Exception($"строка {line}: надо так -> записать \"https://base.firebaseio.com\" \"путь\" \"значение\"");
        string baseUrl = CleanStr(EvalFull(parts[0], line));
        string keyPath = CleanStr(EvalFull(parts[1], line));
        object val = EvalFull(parts[2], line);
        string url = baseUrl.TrimEnd('/') + "/" + keyPath.Trim().TrimStart('/').TrimEnd('/') + ".json";
        string json = ToFirebaseJson(val);
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = Http.PutAsync(url, content).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"статус {(int)resp.StatusCode} ({resp.ReasonPhrase})");
            }
        }
        catch (Exception ex) when (!ex.Message.StartsWith("строка"))
        {
            throw new Exception($"строка {line}: ошибка Firebase записать '{url}': {ex.Message}");
        }
    }

    static void ExecFirebaseRead(string text, int line)
    {
        string rest = AfterFirstWord(text).Trim();
        if (!SplitTargetVar(rest, out string beforeArrow, out string varName) || string.IsNullOrWhiteSpace(varName))
            throw new Exception($"строка {line}: надо так -> прочитать \"https://base.firebaseio.com\" \"путь\" → переменная");
        if (!NameRegex.IsMatch(varName))
            throw new Exception($"строка {line}: плохое имя переменной '{varName}'");
        var parts = SplitArgsPreservingQuotes(beforeArrow);
        if (parts.Count < 2)
            throw new Exception($"строка {line}: надо так -> прочитать \"https://base.firebaseio.com\" \"путь\" → {varName}");
        string baseUrl = CleanStr(EvalFull(parts[0], line));
        string keyPath = CleanStr(EvalFull(parts[1], line));
        string url = baseUrl.TrimEnd('/') + "/" + keyPath.Trim().TrimStart('/').TrimEnd('/') + ".json";
        try
        {
            string resp = Http.GetStringAsync(url).GetAwaiter().GetResult();
            SetVar(varName, FromFirebaseJson(resp));
        }
        catch (Exception ex)
        {
            throw new Exception($"строка {line}: ошибка Firebase прочитать '{url}': {ex.Message}");
        }
    }

    static void ExecFirebaseDelete(string text, int line)
    {
        string rest = AfterFirstWord(text).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2)
            throw new Exception($"строка {line}: надо так -> удалить \"https://base.firebaseio.com\" \"путь\"");
        string baseUrl = CleanStr(EvalFull(parts[0], line));
        string keyPath = CleanStr(EvalFull(parts[1], line));
        string url = baseUrl.TrimEnd('/') + "/" + keyPath.Trim().TrimStart('/').TrimEnd('/') + ".json";
        try
        {
            using var resp = Http.DeleteAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"статус {(int)resp.StatusCode} ({resp.ReasonPhrase})");
            }
        }
        catch (Exception ex) when (!ex.Message.StartsWith("строка"))
        {
            throw new Exception($"строка {line}: ошибка Firebase удалить '{url}': {ex.Message}");
        }
    }
    static string ExtractEventDef(string text)
    {
        string t = Regex.Replace(text.Trim(), @"^когда\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^будет\s+", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^получено\s+", "", RegexOptions.IgnoreCase).Trim();
        t = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return t.ToLowerInvariant();
    }
    static string ExtractBroadcast(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^вещать\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^всем\s+", "", RegexOptions.IgnoreCase).Trim();
        string ev = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (ev == "") throw new Exception($"строка {line}: надо так -> вещать всем привет");
        return ev.ToLowerInvariant();
    }

    static List<Line> curCodeForFind = new();
    static (int end, int elseIdx) FindBlock(List<Line> code, int start) => FindBlockBounded(code, start, code.Count);

    static (int end, int elseIdx) FindBlockBounded(List<Line> code, int start, int bound)
    {
        curCodeForFind = code;
        int depth = 0, elseIdx = -1;
        bool elsePlain = false;
        for (int j = start + 1; j < bound; j++)
        {
            string t = code[j].Text;
            if (IsBlockStart(t)) depth++;
            else if (IsEnd(t))
            {
                if (depth == 0) return (j, elseIdx);
                depth--;
            }
            else if (IsCatch(t) && depth == 0 && IsTry(code[start].Text))
            {
                if (elseIdx >= 0) throw new Exception($"строка {code[j].No}: два 'поймать' в одном 'попробовать'");
                elseIdx = j;
            }
            else if (IsElse(t) && depth == 0 && (IsIf(code[start].Text) || IsElseIf(code[start].Text)))
            {
                if (elseIdx < 0) { elseIdx = j; elsePlain = !IsElseIf(t); }
                else if (!IsElseIf(t) && elsePlain) throw new Exception($"строка {code[j].No}: два 'иначе' в одном 'если'");
            }
        }
        throw new Exception($"строка {code[start].No}: нет 'конец' для '{code[start].Text}'");
    }

    static (int end, int catchIdx) FindTryBlock(List<Line> code, int start, int bound)
    {
        var (end, ci) = FindBlockBounded(code, start, bound);
        return (end, ci);
    }

    static string CondFromIfLine(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^иначе\b", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"^(если|эсли)\b", "", RegexOptions.IgnoreCase).Trim();
        t = StripTrailingWord(t, "то", "тогда");
        if (t == "") throw new Exception($"строка {line}: после 'если' надо условие. Пример: если возраст > 10 то");
        return t;
    }

    static int ExecIfAt(List<Line> code, int idx, int bound)
    {
        var (end, el) = FindBlockBounded(code, idx, bound);
        int no = code[idx].No;
        if (EvalCondition(CondFromIfLine(code[idx].Text, no), no)) ExecRange(code, idx + 1, el >= 0 ? el : end);
        else if (el >= 0)
        {
            if (IsElseIf(code[el].Text)) ExecIfAt(code, el, end + 1);
            else ExecRange(code, el + 1, end);
        }
        return end + 1;
    }

    static string CondFromTriggerLine(string text, int line)
    {
        string t = Regex.Replace(text.Trim(), @"^как\s+только\b", "", RegexOptions.IgnoreCase).Trim();
        t = StripTrailingWord(t, "то", "тогда");
        if (t == "") throw new Exception($"строка {line}: надо так -> как только очки равно 10 то");
        return t;
    }

    static void CollectStarts(List<Line> code)
    {
        for (int i = 0; i < code.Count; i++)
        {
            if (IsOnStart(code[i].Text))
            {
                var (end, _) = FindBlock(code, i);
                OnStarts.Add(code.GetRange(i + 1, end - i - 1));
                i = end;
            }
        }
    }

    static void CollectTriggers(List<Line> code)
    {
        for (int i = 0; i < code.Count; i++)
        {
            if (IsTrigger(code[i].Text))
            {
                string c = CondFromTriggerLine(code[i].Text, code[i].No);
                var (end, _) = FindBlock(code, i);
                Triggers.Add(new Trigger { Cond = c, Body = code.GetRange(i + 1, end - i - 1), No = code[i].No });
                i = end;
            }
        }
    }

    static void CheckTriggers(int line)
    {
        if (Triggers.Count == 0) return;
        if (++TriggerDepth > 20) { TriggerDepth--; throw new Exception($"строка {line}: триггеры зациклились"); }
        try
        {
            foreach (var tr in Triggers)
            {
                bool now;
                try { now = EvalCondition(tr.Cond, line); }
                catch { continue; }
                if (!tr.Last && now)
                {
                    tr.Last = true;
                    ExecRange(tr.Body, 0, tr.Body.Count);
                }
                else tr.Last = now;
            }
        }
        finally { TriggerDepth--; }
    }
    static void CollectHandlers(List<Line> code)
    {
        for (int i = 0; i < code.Count; i++)
        {
            if (IsWhen(code[i].Text))
            {
                string ev = ExtractEventDef(code[i].Text);
                if (ev == "") throw new Exception($"строка {code[i].No}: надо так -> когда получено привет");
                var (end, _) = FindBlock(code, i);
                var body = code.GetRange(i + 1, end - i - 1);
                if (!Handlers.TryGetValue(ev, out var list)) Handlers[ev] = list = new();
                list.Add(body);
                i = end;
            }
        }
    }

    static void Broadcast(string ev, int line)
    {
        if (!Handlers.TryGetValue(ev, out var list)) return;
        if (++BroadcastDepth > 20) { BroadcastDepth--; throw new Exception($"строка {line}: вещание зациклилось ('{ev}')"); }
        foreach (var body in list)
            ExecRange(body, 0, body.Count);
        BroadcastDepth--;
    }

    static void ExecRange(List<Line> code, int from, int to)
    {
        int i = from;
        while (i < to)
        {
            CheckTriggers(code[i].No);
            string text = code[i].Text;
            int no = code[i].No;
            if (IsWhen(text) || IsOnStart(text) || IsOnClick(text) || IsTrigger(text) || IsKeyHandler(text) || IsOnCollision(text) || IsOnOffScreen(text) || IsOnRest(text)) { var (e, _) = FindBlock(code, i); i = e + 1; continue; }
            else if (IsDraw(text)) { ExecDraw(text, no); i++; continue; }
            else if (IsCreateObject(text)) { ExecCreateObject(text, no); i++; continue; }
            else if (IsAssignImage(text)) { ExecAssignImage(text, no); i++; continue; }
            else if (IsAssignProp(text)) { ExecAssignProp(text, no); i++; continue; }
            else if (IsIf(text)) { i = ExecIfAt(code, i, to); continue; }
            else if (IsWhile(text))
            {
                var (e, _) = FindBlock(code, i);
                string c = ExtractWhileCond(text, no);
                int guard = 0;
                while (EvalCondition(c, no))
                {
                    try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } catch (ContinueException) { }
                    if (++guard > 1_000_000) throw new Exception($"строка {no}: 'пока' крутится слишком долго. Добавь 'остановить'");
                }
                i = e + 1; continue;
            }
            else if (IsForever(text))
            {
                var (e, _) = FindBlock(code, i);
                int guard = 0;
                while (true)
                {
                    try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } catch (ContinueException) { }
                    if (++guard > 10_000_000) throw new Exception($"строка {no}: 'вечно повторять' без 'остановить'");
                }
                i = e + 1; continue;
            }
            else if (IsRepeat(text))
            {
                var (e, _) = FindBlock(code, i);
                int n = EvalRepeatCount(text, no);
                for (int k = 0; k < n; k++)
                { try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } catch (ContinueException) { } }
                i = e + 1; continue;
            }
            else if (IsFor(text))
            {
                var (e, _) = FindBlock(code, i);
                var (name, a, b, s) = ParseFor(text, no);
                if (s > 0) { for (double v = a; v <= b + 1e-9; v += s) { Vars[name] = NormNum(v); try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } catch (ContinueException) { } } }
                else { for (double v = a; v >= b - 1e-9; v += s) { Vars[name] = NormNum(v); try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } catch (ContinueException) { } } }
                i = e + 1; continue;
            }
            else if (IsForEach(text))
            {
                var (e, _) = FindBlock(code, i);
                var (name, items) = ParseForEach(text, no);
                foreach (var item in items)
                {
                    Vars[name] = item;
                    try { ExecRange(code, i + 1, e); } catch (BreakException) { break; } catch (ContinueException) { }
                }
                i = e + 1; continue;
            }
            else if (IsElse(text)) throw new Exception($"строка {no}: 'иначе' без 'если'");
            else if (IsEnd(text)) throw new Exception($"строка {no}: лишний 'конец'");
            else if (IsBreak(text)) throw new BreakException();
            else if (IsBroadcast(text)) { Broadcast(ExtractBroadcast(text, no), no); i++; continue; }
            else if (IsSetMotionType(text)) { ExecSetMotionType(text, no); i++; continue; }
            else if (IsSetSceneGravity(text)) { ExecSetSceneGravity(text, no); i++; continue; }
            else if (IsSetSpeedByDirection(text)) { ExecSetSpeedByDirection(text, no); i++; continue; }
            else if (IsSetVelocityX(text)) { ExecSetVelocityX(text, no); i++; continue; }
            else if (IsSetVelocityY(text)) { ExecSetVelocityY(text, no); i++; continue; }
            else if (IsSetVelocity(text)) { ExecSetVelocity(text, no); i++; continue; }
            else if (IsAddVelocity(text)) { ExecAddVelocity(text, no); i++; continue; }
            else if (IsSetAcceleration(text)) { ExecSetAcceleration(text, no); i++; continue; }
            else if (IsStopMotion(text)) { ExecStopMotion(text, no); i++; continue; }
            else if (IsMoveBy(text)) { ExecMoveBy(text, no); i++; continue; }
            else if (IsMoveTo(text)) { ExecMoveTo(text, no); i++; continue; }
            else if (IsSetAngle(text)) { ExecSetAngle(text, no); i++; continue; }
            else if (IsRotateBy(text)) { ExecRotateBy(text, no); i++; continue; }
            else if (IsSetAngularVelocity(text)) { ExecSetAngularVelocity(text, no); i++; continue; }
            else if (IsApplyForce(text)) { ExecApplyForce(text, no); i++; continue; }
            else if (IsApplyExplosion(text)) { ExecApplyExplosion(text, no); i++; continue; }
            else if (IsAttractTo(text)) { ExecAttractTo(text, no); i++; continue; }
            else if (IsSetMass(text)) { ExecSetMass(text, no); i++; continue; }
            else if (IsSetDamping(text)) { ExecSetDamping(text, no); i++; continue; }
            else if (IsSetElasticity(text)) { ExecSetElasticity(text, no); i++; continue; }
            else if (IsSetFriction(text)) { ExecSetFriction(text, no); i++; continue; }
            else if (IsSetMaxSpeed(text)) { ExecSetMaxSpeed(text, no); i++; continue; }
            else if (IsFreezeX(text)) { ExecFreezeX(text, no); i++; continue; }
            else if (IsFreezeY(text)) { ExecFreezeY(text, no); i++; continue; }
            else if (IsUnfreeze(text)) { ExecUnfreeze(text, no); i++; continue; }
            else if (IsSetConstraintX(text)) { ExecSetConstraintX(text, no); i++; continue; }
            else if (IsSetConstraintY(text)) { ExecSetConstraintY(text, no); i++; continue; }
            else if (IsAttachTo(text)) { ExecAttachTo(text, no); i++; continue; }
            else if (IsDetach(text)) { ExecDetach(text, no); i++; continue; }
            else if (IsSetCollisionLayer(text)) { ExecSetCollisionLayer(text, no); i++; continue; }
            else if (IsSetCollisionMask(text)) { ExecSetCollisionMask(text, no); i++; continue; }
            else if (IsIgnoreCollision(text)) { ExecIgnoreCollision(text, no); i++; continue; }
            else if (IsRestoreCollision(text)) { ExecRestoreCollision(text, no); i++; continue; }
            else if (IsSceneBorder(text)) { ExecSceneBorder(text, no); i++; continue; }
            else if (IsBounceOffEdge(text)) { ExecBounceOffEdge(text, no); i++; continue; }
            else if (IsSmoothMoveTo(text)) { ExecSmoothMoveTo(text, no); i++; continue; }
            else if (IsGetDistance(text)) { ExecGetDistance(text, no); i++; continue; }
            else if (IsGetAngleTo(text)) { ExecGetAngleTo(text, no); i++; continue; }
            else if (IsCheckCollision(text)) { ExecCheckCollision(text, no); i++; continue; }
            else if (IsObjectOnScreen(text)) { ExecObjectOnScreen(text, no); i++; continue; }
            else if (IsGetSpeed(text)) { ExecGetSpeed(text, no); i++; continue; }
            else if (IsSetDirection(text)) { ExecSetDirection(text, no); i++; continue; }
            else if (IsSet(text)) { ExecSetGeneric(text, no); i++; continue; }
            else if (IsShowMessage(text)) { ExecShowMessage(text, no); i++; continue; }
            else if (IsShowError(text)) { ExecShowError(text, no); i++; continue; }
            else if (IsPrint(text))
            {
                string rest = AfterFirstWord(text);
                if (rest == "") throw new Exception($"строка {no}: что вывести? пример: вывести иван");
                try { Console.WriteLine(Fmt(EvalFull(rest, no))); }
                catch (Exception ex) when (!ex.Message.StartsWith("строка"))
                { throw new Exception($"строка {no}: {ex.Message}"); }
                i++; continue;
            }
            else if (IsCreateList(text))
            {
                string rest = AfterFirstWord(text);
                var parts = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || !parts[0].Equals("список", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"строка {no}: надо так -> создать список фрукты");
                if (!NameRegex.IsMatch(parts[1])) throw new Exception($"строка {no}: плохое имя '{parts[1]}'");
                Vars[parts[1]] = new List<object>();
                i++; continue;
            }
            else if (IsAdd(text))
            {
                string rest = AfterFirstWord(text);
                rest = Regex.Replace(rest, @"^в\s+", "", RegexOptions.IgnoreCase).Trim();
                int sp = rest.IndexOfAny(new[] { ' ', '\t' });
                if (sp < 0) throw new Exception($"строка {no}: надо так -> добавить в фрукты \"вишня\"");
                string name = rest[..sp].Trim();
                string valExpr = rest[(sp + 1)..].Trim();
                if (valExpr == "") throw new Exception($"строка {no}: надо так -> добавить в {name} \"вишня\"");
                if (!Vars.TryGetValue(name, out var v) || v is not List<object> l)
                    throw new Exception($"строка {no}: нет такого списка: {name}. Сначала: создать список {name}");
                l.Add(EvalFull(valExpr, no));
                i++; continue;
            }
            else if (IsAskYesNo(text)) { ExecAskYesNo(text, no); i++; continue; }
            else if (IsAsk(text)) { ExecAsk(text, no); i++; continue; }
            else if (IsWait(text)) { ExecWait(text, no); i++; continue; }
            else if (IsCreateWindow(text)) { ExecCreateWindow(text, no); i++; continue; }
            else if (IsRunScene(text)) { ExecRunScene(text, no); i++; continue; }
            else if (IsRunScript(text)) { ExecRunScript(text, no); i++; continue; }
            else if (IsStopScripts(text)) { ExecStopScripts(); i++; continue; }
            else if (IsStopScript(text)) { ExecStopScript(text, no); i++; continue; }
            else if (IsFirebaseDelete(text)) { ExecFirebaseDelete(text, no); i++; continue; }
            else if (IsDeleteFile(text)) { ExecDeleteFile(text, no); i++; continue; }
            else if (IsRenameFile(text)) { ExecRenameFile(text, no); i++; continue; }
            else if (IsCopyFile(text)) { ExecCopyFile(text, no); i++; continue; }
            else if (IsListFiles(text)) { ExecListFiles(text, no); i++; continue; }
            else if (IsCreateTable(text)) { ExecCreateTable(text, no); i++; continue; }
            else if (IsDeleteFromTable(text)) { ExecDeleteFromTable(text, no); i++; continue; }
            else if (IsDelete(text)) { ExecDelete(text, no); i++; continue; }
            else if (IsShuffle(text)) { ExecShuffle(text, no); i++; continue; }
            else if (IsPasteClipboard(text)) { ExecPasteClipboard(text, no); i++; continue; }
            else if (IsInsert(text)) { ExecInsert(text, no); i++; continue; }
            else if (IsClear(text)) { ExecClear(text, no); i++; continue; }
            else if (IsEvery(text)) { i = ExecEveryAt(code, i, to); continue; }
            else if (IsTry(text)) { i = ExecTryAt(code, i, to); continue; }
            else if (IsCatch(text)) throw new Exception($"строка {no}: 'поймать' без 'попробовать'");
            else if (IsCreateFolder(text)) { ExecCreateFolder(text, no); i++; continue; }
            else if (IsSplit(text)) { ExecSplit(text, no); i++; continue; }
            else if (IsJoin(text)) { ExecJoin(text, no); i++; continue; }
            else if (IsFirebaseWrite(text)) { ExecFirebaseWrite(text, no); i++; continue; }
            else if (IsWriteFile(text)) { ExecWriteFile(text, no); i++; continue; }
            else if (IsFirebaseRead(text)) { ExecFirebaseRead(text, no); i++; continue; }
            else if (IsReadFile(text)) { ExecReadFile(text, no); i++; continue; }
            else if (IsStopSound(text)) { ExecStopSound(text, no); i++; continue; }
            else if (IsPauseSound(text)) { ExecPauseSound(text, no); i++; continue; }
            else if (IsResumeSound(text)) { ExecResumeSound(text, no); i++; continue; }
            else if (IsSetVolume(text)) { ExecSetVolume(text, no); i++; continue; }
            else if (IsPlaySound(text)) { ExecPlaySound(text, no); i++; continue; }
            else if (IsCopyClipboard(text)) { ExecCopyClipboard(text, no); i++; continue; }
            else if (IsHttpGet(text)) { ExecHttpGet(text, no); i++; continue; }
            else if (IsHttpSend(text)) { ExecHttpSend(text, no); i++; continue; }
            else if (IsContinue(text)) throw new ContinueException();
            else if (IsExit(text)) throw new ExitException();
            else throw new Exception($"строка {no}: не знаю команду '{text}'. Знаю: задать, вывести, спросить, ждать, воспроизвести/играть звук, остановить/пауза/продолжить звук, громкость, показать сообщение/ошибку, скопировать, вставить, получить, отправить, создать окно/список/таблицу/папку, запустить сцену/скрипт, остановить скрипты, каждые, если, иначе если, пока, повтори, для, продолжить, остановить, выход, когда, вещать, добавить, удалить, удалить файл, переименовать, скопировать файл, список файлов, вставить, очистить, перемешать, разделить, склеить, записать, прочитать, подключить, при запуске/нажатии, как только, попробовать");
        }
    }

    static int ExecTryAt(List<Line> code, int idx, int bound)
    {
        var (end, ci) = FindTryBlock(code, idx, bound);
        int no = code[idx].No;
        try
        {
            ExecRange(code, idx + 1, ci >= 0 ? ci : end);
        }
        catch (BreakException) { throw; }
        catch (ContinueException) { throw; }
        catch (ExitException) { throw; }
        catch (Exception ex)
        {
            if (ci >= 0)
            {
                string varPart = AfterFirstWord(code[ci].Text).Trim();
                if (varPart != "" && NameRegex.IsMatch(varPart))
                    Vars[varPart] = ex.Message;
                ExecRange(code, ci + 1, end);
            }
            else throw;
        }
        return end + 1;
    }

    static int ExecEveryAt(List<Line> code, int idx, int bound)
    {
        var (end, _) = FindBlock(code, idx);
        double sec = ParseEverySec(code[idx].Text, code[idx].No);
        var body = code.GetRange(idx + 1, end - idx - 1);
        new System.Threading.Thread(() =>
        {
            try
            {
                while (true)
                {
                    System.Threading.Thread.Sleep((int)(sec * 1000));
                    lock (VarsLock) { ExecRange(body, 0, body.Count); }
                }
            }
            catch (Exception ex) { Console.Error.WriteLine("[каждые] " + ex.Message); }
        }) { IsBackground = true }.Start();
        return end + 1;
    }

    static List<Line> LoadWithIncludes(string path, HashSet<string> visited)
    {
        string full;
        try { full = System.IO.Path.GetFullPath(path); }
        catch { throw new Exception($"плохое имя файла: '{path}'"); }
        if (!visited.Add(full)) throw new Exception($"круг: файл '{path}' уже подключен");
        string[] raw;
        try { raw = File.ReadAllLines(full, System.Text.Encoding.UTF8); }
        catch (Exception ex) { throw new Exception($"не открыть '{path}': {ex.Message}"); }
        string dir = System.IO.Path.GetDirectoryName(full) ?? "";
        var code = new List<Line>();
        for (int i = 0; i < raw.Length; i++)
        {
            string t = raw[i].Trim();
            if (t == "" || t.StartsWith("#") || t.StartsWith("//")) continue;
            if (IsInclude(t))
            {
                string arg = AfterFirstWord(t).Trim();
                if (arg.StartsWith("\"") && arg.EndsWith("\"") && arg.Length >= 2) arg = arg[1..^1];
                if (arg == "") throw new Exception($"строка {i + 1}: надо так -> подключить \"библио.ncode\"");
                code.AddRange(LoadWithIncludes(System.IO.Path.Combine(dir, arg), visited));
                continue;
            }
            code.Add(new Line(t, i + 1));
        }
        return code;
    }

    static void ExecCreateTable(string text, int line)
    {
        string rest = AfterFirstWord(text).Trim();
        var parts = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("таблицу", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"строка {line}: надо так -> создать таблицу игрок");
        if (!NameRegex.IsMatch(parts[1])) throw new Exception($"строка {line}: плохое имя '{parts[1]}'");
        Vars[parts[1]] = new Dictionary<string, object>(StringComparer.Ordinal);
    }

    static void ExecDeleteFromTable(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^удал(ить|и)\s+из\s+таблицы\b", "", RegexOptions.IgnoreCase).Trim();
        int sp = rest.IndexOfAny(new[] { ' ', '\t' });
        if (sp < 0) throw new Exception($"строка {line}: надо так -> удалить из таблицы игрок \"имя\"");
        string tableName = rest[..sp].Trim();
        string keyExpr = rest[(sp + 1)..].Trim();
        if (!TryResolveVar(tableName, out var tv) || tv is not Dictionary<string, object> dict)
            throw new Exception($"строка {line}: '{tableName}' не таблица");
        string key = Fmt(EvalFull(keyExpr, line));
        dict.Remove(key);
    }

    static void ExecDeleteFile(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^удал(ить|и)\s+файл\b", "", RegexOptions.IgnoreCase).Trim();
        if (rest == "") throw new Exception($"строка {line}: надо так -> удалить файл \"save.txt\"");
        string path = Fmt(EvalFull(rest, line));
        try { File.Delete(path); }
        catch (Exception ex) { throw new Exception($"строка {line}: не удалить файл '{path}': {ex.Message}"); }
    }

    static void ExecRenameFile(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^переименовать\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> переименовать \"old.txt\" \"new.txt\"");
        string from = Fmt(EvalFull(parts[0], line));
        string to = Fmt(EvalFull(parts[1], line));
        try { File.Move(from, to, true); }
        catch (Exception ex) { throw new Exception($"строка {line}: не переименовать '{from}': {ex.Message}"); }
    }

    static void ExecCopyFile(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^скопировать\s+файл\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> скопировать файл \"a.txt\" \"b.txt\"");
        string from = Fmt(EvalFull(parts[0], line));
        string to = Fmt(EvalFull(parts[1], line));
        try { File.Copy(from, to, true); }
        catch (Exception ex) { throw new Exception($"строка {line}: не скопировать файл '{from}': {ex.Message}"); }
    }

    static void ExecListFiles(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^список\s+файлов\b", "", RegexOptions.IgnoreCase).Trim();
        if (!SplitTargetVar(rest, out string pathExpr, out string varName) || string.IsNullOrWhiteSpace(varName))
            throw new Exception($"строка {line}: надо так -> список файлов \"папка\" → список");
        string dir = Fmt(EvalFull(pathExpr, line));
        if (dir == "") dir = curDir;
        try
        {
            var files = Directory.GetFiles(dir).Select(f => (object)Path.GetFileName(f)).ToList();
            if (!NameRegex.IsMatch(varName)) throw new Exception($"строка {line}: плохое имя переменной '{varName}'");
            Vars[varName] = files;
        }
        catch (Exception ex) when (!ex.Message.StartsWith("строка"))
        {
            throw new Exception($"строка {line}: не получить список файлов '{dir}': {ex.Message}");
        }
    }

    class CollisionHandlerDef
    {
        public string Obj1 = "";
        public string Obj2 = "";
        public List<Line> Body = new();
    }
    class OffScreenHandlerDef
    {
        public string Target = "";
        public List<Line> Body = new();
    }
    class RestHandlerDef
    {
        public string Target = "";
        public List<Line> Body = new();
    }
    static readonly List<CollisionHandlerDef> PhysicsCollisionHandlers = new();
    static readonly List<OffScreenHandlerDef> PhysicsOffScreenHandlers = new();
    static readonly List<RestHandlerDef> PhysicsRestHandlers = new();

    static void CollectPhysicsHandlers(List<Line> code)
    {
        for (int i = 0; i < code.Count; i++)
        {
            string text = code[i].Text;
            if (IsOnCollision(text))
            {
                var (end, _) = FindBlock(code, i);
                var body = code.GetRange(i + 1, end - i - 1);
                string rest = Regex.Replace(text.Trim(), @"^при\s+столкновении\b", "", RegexOptions.IgnoreCase).Trim();
                rest = StripTrailingWord(rest, "то", "тогда");
                var parts = SplitArgsPreservingQuotes(rest);
                string o1 = parts.Count > 0 ? parts[0] : "";
                string o2 = parts.Count > 1 ? parts[1] : "";
                lock (PhysicsCollisionHandlers)
                {
                    PhysicsCollisionHandlers.Add(new CollisionHandlerDef { Obj1 = o1, Obj2 = o2, Body = body });
                }
                i = end;
            }
            else if (IsOnOffScreen(text))
            {
                var (end, _) = FindBlock(code, i);
                var body = code.GetRange(i + 1, end - i - 1);
                string rest = Regex.Replace(text.Trim(), @"^при\s+выходе\s+за\s+экран\b", "", RegexOptions.IgnoreCase).Trim();
                rest = StripTrailingWord(rest, "то", "тогда");
                string target = SplitArgsPreservingQuotes(rest).FirstOrDefault() ?? "";
                lock (PhysicsOffScreenHandlers)
                {
                    PhysicsOffScreenHandlers.Add(new OffScreenHandlerDef { Target = target, Body = body });
                }
                i = end;
            }
            else if (IsOnRest(text))
            {
                var (end, _) = FindBlock(code, i);
                var body = code.GetRange(i + 1, end - i - 1);
                string rest = Regex.Replace(text.Trim(), @"^при\s+покое\b", "", RegexOptions.IgnoreCase).Trim();
                rest = StripTrailingWord(rest, "то", "тогда");
                string target = SplitArgsPreservingQuotes(rest).FirstOrDefault() ?? "";
                lock (PhysicsRestHandlers)
                {
                    PhysicsRestHandlers.Add(new RestHandlerDef { Target = target, Body = body });
                }
                i = end;
            }
        }
    }

    static void FireCollisionHandlers(string o1, string o2)
    {
        List<CollisionHandlerDef> toRun = new();
        lock (PhysicsCollisionHandlers)
        {
            foreach (var ch in PhysicsCollisionHandlers)
            {
                bool match1 = string.IsNullOrEmpty(ch.Obj1) || ch.Obj1.Equals(o1, StringComparison.OrdinalIgnoreCase);
                bool match2 = string.IsNullOrEmpty(ch.Obj2) || ch.Obj2.Equals(o2, StringComparison.OrdinalIgnoreCase);
                bool matchRev1 = string.IsNullOrEmpty(ch.Obj1) || ch.Obj1.Equals(o2, StringComparison.OrdinalIgnoreCase);
                bool matchRev2 = string.IsNullOrEmpty(ch.Obj2) || ch.Obj2.Equals(o1, StringComparison.OrdinalIgnoreCase);

                if ((match1 && match2) || (matchRev1 && matchRev2))
                    toRun.Add(ch);
            }
        }
        foreach (var ch in toRun)
        {
            lock (VarsLock)
            {
                Vars["объект_1"] = o1;
                Vars["объект_2"] = o2;
                Vars["обьект_1"] = o1;
                Vars["обьект_2"] = o2;
            }
            try { ExecRange(ch.Body, 0, ch.Body.Count); } catch (Exception ex) { Console.Error.WriteLine("[событие] " + ex.Message); }
        }
    }

    static void FireOffScreenHandlers(string obj)
    {
        List<OffScreenHandlerDef> toRun = new();
        lock (PhysicsOffScreenHandlers)
        {
            foreach (var h in PhysicsOffScreenHandlers)
            {
                if (string.IsNullOrEmpty(h.Target) || h.Target.Equals(obj, StringComparison.OrdinalIgnoreCase))
                    toRun.Add(h);
            }
        }
        foreach (var h in toRun)
        {
            lock (VarsLock)
            {
                Vars["объект"] = obj;
                Vars["обьект"] = obj;
            }
            try { ExecRange(h.Body, 0, h.Body.Count); } catch (Exception ex) { Console.Error.WriteLine("[событие] " + ex.Message); }
        }
    }

    static void FireRestHandlers(string obj)
    {
        List<RestHandlerDef> toRun = new();
        lock (PhysicsRestHandlers)
        {
            foreach (var h in PhysicsRestHandlers)
            {
                if (string.IsNullOrEmpty(h.Target) || h.Target.Equals(obj, StringComparison.OrdinalIgnoreCase))
                    toRun.Add(h);
            }
        }
        foreach (var h in toRun)
        {
            lock (VarsLock)
            {
                Vars["объект"] = obj;
                Vars["обьект"] = obj;
            }
            try { ExecRange(h.Body, 0, h.Body.Count); } catch (Exception ex) { Console.Error.WriteLine("[событие] " + ex.Message); }
        }
    }

    static double PhysicsGravityX = 0;
    static double PhysicsGravityY = 0;
    static double PhysicsInterval = 0.05;
    static bool PhysicsRunning = false;
    static Thread? PhysicsThread = null;
    static readonly object PhysicsLock = new();
    static bool SceneBorderEnabled = false;

    static void StartPhysics()
    {
        lock (PhysicsLock)
        {
            if (PhysicsRunning) return;
            PhysicsRunning = true;
            PhysicsThread = new Thread(PhysicsLoop)
            {
                IsBackground = true,
                Name = "Ncode:Physics"
            };
            PhysicsThread.Start();
        }
    }

    static void StopPhysics()
    {
        lock (PhysicsLock)
        {
            PhysicsRunning = false;
            PhysicsGravityX = 0;
            PhysicsGravityY = 0;
        }
    }

    static bool BoxesIntersect(double x1, double y1, double w1, double h1, double x2, double y2, double w2, double h2)
    {
        return x1 < x2 + w2 && x1 + w1 > x2 && y1 < y2 + h2 && y1 + h1 > y2;
    }

    static GameObject? MoveDynamicObjectWithCollision(GameObject dyn, double dx, double dy)
    {
        GameObject? collidedWith = null;
        var staticList = new List<GameObject>();
        lock (GameObjectsLock)
        {
            foreach (var o in GameObjects.Values)
            {
                if (o != dyn && o.Motion == ObjectMotion.Static && o.Visible)
                {
                    if (dyn.IgnoredCollisions.Contains(o.Name) || o.IgnoredCollisions.Contains(dyn.Name))
                        continue;
                    if ((dyn.CollisionLayer & o.CollisionMask) == 0 && (o.CollisionLayer & dyn.CollisionMask) == 0)
                        continue;
                    staticList.Add(o);
                }
            }
        }

        if (Math.Abs(dy) > 1e-9)
        {
            double targetY = dyn.Y + dy;
            var db = dyn.GetBounds();
            foreach (var st in staticList)
            {
                var sb = st.GetBounds();
                if (BoxesIntersect(db.X, targetY, db.Width, db.Height, sb.X, sb.Y, sb.Width, sb.Height))
                {
                    collidedWith = st;
                    if (dy > 0 && db.Y + db.Height <= sb.Y + 1e-6 + Math.Abs(dy))
                    {
                        targetY = Math.Min(targetY, sb.Y - db.Height);
                        if (dyn.Elasticity > 0)
                            dyn.VelocityY = -Math.Abs(dyn.VelocityY) * dyn.Elasticity;
                        else
                            dyn.VelocityY = 0;
                        dyn.VelocityX *= Math.Max(0, 1.0 - dyn.Friction);
                    }
                    else if (dy < 0 && db.Y >= sb.Y + sb.Height - 1e-6 - Math.Abs(dy))
                    {
                        targetY = Math.Max(targetY, sb.Y + sb.Height);
                        if (dyn.Elasticity > 0)
                            dyn.VelocityY = Math.Abs(dyn.VelocityY) * dyn.Elasticity;
                        else
                            dyn.VelocityY = 0;
                        dyn.VelocityX *= Math.Max(0, 1.0 - dyn.Friction);
                    }
                }
            }
            dyn.Y = targetY;
        }

        if (Math.Abs(dx) > 1e-9)
        {
            double targetX = dyn.X + dx;
            var db = dyn.GetBounds();
            foreach (var st in staticList)
            {
                var sb = st.GetBounds();
                if (BoxesIntersect(targetX, db.Y, db.Width, db.Height, sb.X, sb.Y, sb.Width, sb.Height))
                {
                    collidedWith = st;
                    if (dx > 0 && db.X + db.Width <= sb.X + 1e-6 + Math.Abs(dx))
                    {
                        targetX = Math.Min(targetX, sb.X - db.Width);
                        if (dyn.Elasticity > 0)
                            dyn.VelocityX = -Math.Abs(dyn.VelocityX) * dyn.Elasticity;
                        else
                            dyn.VelocityX = 0;
                        dyn.VelocityY *= Math.Max(0, 1.0 - dyn.Friction);
                    }
                    else if (dx < 0 && db.X >= sb.X + sb.Width - 1e-6 - Math.Abs(dx))
                    {
                        targetX = Math.Max(targetX, sb.X + sb.Width);
                        if (dyn.Elasticity > 0)
                            dyn.VelocityX = Math.Abs(dyn.VelocityX) * dyn.Elasticity;
                        else
                            dyn.VelocityX = 0;
                        dyn.VelocityY *= Math.Max(0, 1.0 - dyn.Friction);
                    }
                }
            }
            dyn.X = targetX;
        }

        return collidedWith;
    }

    static void PhysicsLoop()
    {
        while (true)
        {
            double gx, gy, interval;
            lock (PhysicsLock)
            {
                if (!PhysicsRunning) break;
                gx = PhysicsGravityX;
                gy = PhysicsGravityY;
                interval = PhysicsInterval;
            }

            int sleepMs = Math.Clamp((int)(interval * 1000), 5, 1000);
            Thread.Sleep(sleepMs);

            double dt = interval;

            List<GameObject> dynamicList = new();
            lock (GameObjectsLock)
            {
                foreach (var o in GameObjects.Values)
                {
                    if (o.Motion == ObjectMotion.Dynamic && o.Visible)
                        dynamicList.Add(o);
                }
            }

            if (dynamicList.Count == 0) continue;

            bool moved = false;
            List<(string obj1, string obj2)> collisionsToFire = new();
            List<string> offScreenToFire = new();
            List<string> restToFire = new();

            int winW = GetCurrentWindowWidth();
            int winH = GetCurrentWindowHeight();

            foreach (var dyn in dynamicList)
            {
                if (!string.IsNullOrEmpty(dyn.AttachedTo))
                {
                    GameObject? parent = null;
                    lock (GameObjectsLock) { GameObjects.TryGetValue(dyn.AttachedTo, out parent); }
                    if (parent != null)
                    {
                        dyn.X = parent.X + dyn.AttachOffsetX;
                        dyn.Y = parent.Y + dyn.AttachOffsetY;
                        moved = true;
                        SyncObjectVars(dyn.Name);
                        continue;
                    }
                }

                if (!dyn.FreezeX)
                {
                    dyn.VelocityX += (dyn.AccelX + gx) * dt;
                    if (dyn.Damping > 0) dyn.VelocityX *= Math.Max(0, 1.0 - dyn.Damping * dt);
                }
                else
                {
                    dyn.VelocityX = 0;
                }

                if (!dyn.FreezeY)
                {
                    dyn.VelocityY += (dyn.AccelY + gy) * dt;
                    if (dyn.Damping > 0) dyn.VelocityY *= Math.Max(0, 1.0 - dyn.Damping * dt);
                }
                else
                {
                    dyn.VelocityY = 0;
                }

                double spd = dyn.Speed;
                if (spd > dyn.MaxSpeed && spd > 1e-9)
                {
                    double scale = dyn.MaxSpeed / spd;
                    dyn.VelocityX *= scale;
                    dyn.VelocityY *= scale;
                }

                if (Math.Abs(dyn.AngularVelocity) > 1e-9)
                {
                    dyn.Angle = (dyn.Angle + dyn.AngularVelocity * dt) % 360.0;
                    if (dyn.Angle < 0) dyn.Angle += 360.0;
                    dyn.Props["угол"] = dyn.Angle;
                    moved = true;
                }

                double stepX = dyn.VelocityX * dt;
                double stepY = dyn.VelocityY * dt;

                double oldX = dyn.X;
                double oldY = dyn.Y;

                var hitStatic = MoveDynamicObjectWithCollision(dyn, stepX, stepY);
                if (hitStatic != null)
                {
                    collisionsToFire.Add((dyn.Name, hitStatic.Name));
                }

                var db = dyn.GetBounds();
                if (SceneBorderEnabled)
                {
                    if (dyn.X < 0)
                    {
                        dyn.X = 0;
                        dyn.VelocityX = Math.Abs(dyn.VelocityX) * (dyn.Elasticity > 0 ? dyn.Elasticity : 1.0);
                    }
                    else if (dyn.X + db.Width > winW)
                    {
                        dyn.X = Math.Max(0, winW - db.Width);
                        dyn.VelocityX = -Math.Abs(dyn.VelocityX) * (dyn.Elasticity > 0 ? dyn.Elasticity : 1.0);
                    }

                    if (dyn.Y < 0)
                    {
                        dyn.Y = 0;
                        dyn.VelocityY = Math.Abs(dyn.VelocityY) * (dyn.Elasticity > 0 ? dyn.Elasticity : 1.0);
                    }
                    else if (dyn.Y + db.Height > winH)
                    {
                        dyn.Y = Math.Max(0, winH - db.Height);
                        dyn.VelocityY = -Math.Abs(dyn.VelocityY) * (dyn.Elasticity > 0 ? dyn.Elasticity : 1.0);
                    }
                }

                if (dyn.X < dyn.ConstraintMinX) { dyn.X = dyn.ConstraintMinX; dyn.VelocityX = 0; }
                if (dyn.X > dyn.ConstraintMaxX) { dyn.X = dyn.ConstraintMaxX; dyn.VelocityX = 0; }
                if (dyn.Y < dyn.ConstraintMinY) { dyn.Y = dyn.ConstraintMinY; dyn.VelocityY = 0; }
                if (dyn.Y > dyn.ConstraintMaxY) { dyn.Y = dyn.ConstraintMaxY; dyn.VelocityY = 0; }

                if (Math.Abs(dyn.X - oldX) > 1e-9 || Math.Abs(dyn.Y - oldY) > 1e-9)
                {
                    moved = true;
                    SyncObjectVars(dyn.Name);
                }

                bool isOff = (dyn.X + db.Width < 0 || dyn.X > winW || dyn.Y + db.Height < 0 || dyn.Y > winH);
                if (isOff)
                {
                    if (!dyn.WasOffScreen)
                    {
                        dyn.WasOffScreen = true;
                        offScreenToFire.Add(dyn.Name);
                    }
                }
                else
                {
                    dyn.WasOffScreen = false;
                }

                if (dyn.Speed < 0.5 && Math.Abs(gx) < 1e-9 && Math.Abs(gy) < 1e-9)
                {
                    if (!dyn.WasAtRest)
                    {
                        dyn.WasAtRest = true;
                        restToFire.Add(dyn.Name);
                    }
                }
                else
                {
                    dyn.WasAtRest = false;
                }
            }

            if (moved) InvalidateGameWindow();

            if (collisionsToFire.Count > 0 || offScreenToFire.Count > 0 || restToFire.Count > 0)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    foreach (var (a, b) in collisionsToFire)
                        FireCollisionHandlers(a, b);
                    foreach (var n in offScreenToFire)
                        FireOffScreenHandlers(n);
                    foreach (var n in restToFire)
                        FireRestHandlers(n);
                });
            }
        }
    }

    static void ExecSetMotionType(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+тип\s+движения\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> задать тип движения мяч динамичный");
        string name = parts[0];
        string typeStr = parts[1].ToLowerInvariant();
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            if (typeStr is "динамичный" or "динамический" or "dynamic")
                obj.Motion = ObjectMotion.Dynamic;
            else if (typeStr is "статичный" or "статический" or "static")
                obj.Motion = ObjectMotion.Static;
            else
                obj.Motion = ObjectMotion.None;
            obj.Props["тип_движения"] = typeStr;
        }
        SyncObjectVars(name);
    }

    static void ExecSetSceneGravity(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+тяжесть\s+сцены\b", "", RegexOptions.IgnoreCase).Trim();
        rest = Regex.Replace(rest, @"^по\s+", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> задать тяжесть сцены по 0 10 0.1");
        double gx = ToNum(EvalArith(parts[0], line), line);
        double gy = ToNum(EvalArith(parts[1], line), line);
        double interval = parts.Count >= 3 ? ToNum(EvalArith(parts[2], line), line) : 0.05;
        if (interval <= 0) interval = 0.05;
        lock (PhysicsLock)
        {
            PhysicsGravityX = gx;
            PhysicsGravityY = gy;
            PhysicsInterval = interval;
        }
        EnsureGameWindow();
        StartPhysics();
    }

    static void ExecSetGeneric(string text, int line)
    {
        string rest = AfterFirstWord(text).Trim();
        string name;
        string val;
        int brOpen = rest.IndexOf('[');
        if (brOpen > 0)
        {
            bool inQ = false;
            int brClose = -1;
            int depth = 0;
            for (int bi = brOpen; bi < rest.Length; bi++)
            {
                if (rest[bi] == '"') { inQ = !inQ; continue; }
                if (inQ) continue;
                if (rest[bi] == '[') depth++;
                else if (rest[bi] == ']')
                {
                    depth--;
                    if (depth == 0) { brClose = bi; break; }
                }
            }
            if (brClose > brOpen)
            {
                name = rest[..(brClose + 1)].Trim();
                val = rest[(brClose + 1)..].Trim();
                string tableName = name[..brOpen].Trim();
                string keyExpr = name[(brOpen + 1)..^1].Trim();
                if (!TryResolveVar(tableName, out var tv) || tv is not Dictionary<string, object> dict)
                    throw new Exception($"строка {line}: '{tableName}' не таблица. Сначала: создать таблицу {tableName}");
                string key = Fmt(EvalFull(keyExpr, line));
                dict[key] = EvalFull(val, line);
                return;
            }
        }
        int sp = rest.IndexOfAny(new[] { ' ', '\t' });
        if (sp < 0) throw new Exception($"строка {line}: надо так -> задать иван 5");
        name = rest[..sp].Trim();
        val = rest[(sp + 1)..].Trim();
        if (val == "") throw new Exception($"строка {line}: нет значения");
        if (!NameRegex.IsMatch(name)) throw new Exception($"строка {line}: плохое имя '{name}'");
        Vars[name] = EvalFull(val, line);
    }

    static bool IsBoolWord(string s)
    {
        return s.ToLowerInvariant() is "истина" or "истинно" or "правда" or "true"
            or "ложь" or "ложно" or "неправда" or "false";
    }

    static bool IsPhysicsTarget(string s)
    {
        if (!NameRegex.IsMatch(s)) return false;
        if (IsBoolWord(s)) return false;
        if (TryResolveVar(s, out _)) return false;
        return true;
    }

    static void ExecSetVelocity(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+скорость\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 3 || !IsPhysicsTarget(parts[0]))
        {
            Exception? assignErr = null;
            try { ExecSetGeneric("задать скорость " + rest, line); return; }
            catch (Exception ex) { assignErr = ex; }
            if (parts.Count == 0 || !NameRegex.IsMatch(parts[0]) || IsBoolWord(parts[0]))
                throw assignErr!;
        }
        string name = parts[0];
        double vx = ToNum(EvalArith(parts[1], line), line);
        double vy = ToNum(EvalArith(parts[2], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.VelocityX = vx;
            obj.VelocityY = vy;
        }
        SyncObjectVars(name);
        StartPhysics();
    }

    static void ExecSetVelocityX(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+скорость\s+[хx]\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> задать скорость х мяч 10");
        string name = parts[0];
        double vx = ToNum(EvalArith(parts[1], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.VelocityX = vx;
        }
        SyncObjectVars(name);
        StartPhysics();
    }

    static void ExecSetVelocityY(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+скорость\s+[уy]\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> задать скорость у мяч -10");
        string name = parts[0];
        double vy = ToNum(EvalArith(parts[1], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.VelocityY = vy;
        }
        SyncObjectVars(name);
        StartPhysics();
    }

    static void ExecAddVelocity(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^доба(вить|вь)\s+скорость\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 3) throw new Exception($"строка {line}: надо так -> добавить скорость мяч 5 -5");
        string name = parts[0];
        double dvx = ToNum(EvalArith(parts[1], line), line);
        double dvy = ToNum(EvalArith(parts[2], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.VelocityX += dvx;
            obj.VelocityY += dvy;
        }
        SyncObjectVars(name);
        StartPhysics();
    }

    static void ExecSetAcceleration(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+ускорение\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 3 || !IsPhysicsTarget(parts[0]))
        {
            Exception? assignErr = null;
            try { ExecSetGeneric("задать ускорение " + rest, line); return; }
            catch (Exception ex) { assignErr = ex; }
            if (parts.Count == 0 || !NameRegex.IsMatch(parts[0]) || IsBoolWord(parts[0]))
                throw assignErr!;
        }
        string name = parts[0];
        double ax = ToNum(EvalArith(parts[1], line), line);
        double ay = ToNum(EvalArith(parts[2], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.AccelX = ax;
            obj.AccelY = ay;
        }
        SyncObjectVars(name);
        StartPhysics();
    }

    static void ExecStopMotion(string text, int line)
    {
        string name = Regex.Replace(text.Trim(), @"^(остановить|останови)\s+движение\b", "", RegexOptions.IgnoreCase).Trim();
        if (name == "") throw new Exception($"строка {line}: надо так -> остановить движение мяч");
        name = SplitArgsPreservingQuotes(name).FirstOrDefault() ?? "";
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(name, out var obj))
            {
                obj.VelocityX = 0;
                obj.VelocityY = 0;
                obj.AccelX = 0;
                obj.AccelY = 0;
                obj.AngularVelocity = 0;
            }
        }
        SyncObjectVars(name);
    }

    static void ExecMoveBy(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^перемест(ить|и)\s+", "", RegexOptions.IgnoreCase).Trim();
        int onIdx = -1;
        var parts = SplitArgsPreservingQuotes(rest);
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Equals("на", StringComparison.OrdinalIgnoreCase)) { onIdx = i; break; }
        }
        if (onIdx < 1 || parts.Count < onIdx + 3)
            throw new Exception($"строка {line}: надо так -> переместить мяч на 10 -5");
        string name = parts[0];
        double dx = ToNum(EvalArith(parts[onIdx + 1], line), line);
        double dy = ToNum(EvalArith(parts[onIdx + 2], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            if (obj.Motion == ObjectMotion.Dynamic)
                MoveDynamicObjectWithCollision(obj, dx, dy);
            else
            {
                obj.X += dx;
                obj.Y += dy;
            }
        }
        SyncObjectVars(name);
        InvalidateGameWindow();
    }

    static void ExecMoveTo(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^перемест(ить|и)\s+", "", RegexOptions.IgnoreCase).Trim();
        int toIdx = -1;
        var parts = SplitArgsPreservingQuotes(rest);
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Equals("в", StringComparison.OrdinalIgnoreCase)) { toIdx = i; break; }
        }
        if (toIdx < 1 || parts.Count < toIdx + 3)
            throw new Exception($"строка {line}: надо так -> переместить мяч в 100 200");
        string name = parts[0];
        double x = ToNum(EvalArith(parts[toIdx + 1], line), line);
        double y = ToNum(EvalArith(parts[toIdx + 2], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.X = x;
            obj.Y = y;
        }
        SyncObjectVars(name);
        InvalidateGameWindow();
    }

    static void ExecSetAngle(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+угол\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2 || !IsPhysicsTarget(parts[0]))
        {
            Exception? assignErr = null;
            try { ExecSetGeneric("задать угол " + rest, line); return; }
            catch (Exception ex) { assignErr = ex; }
            if (parts.Count == 0 || !NameRegex.IsMatch(parts[0]) || IsBoolWord(parts[0]))
                throw assignErr!;
        }
        string name = parts[0];
        double deg = ToNum(EvalArith(parts[1], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.Angle = deg % 360.0;
            if (obj.Angle < 0) obj.Angle += 360.0;
            obj.Props["угол"] = obj.Angle;
        }
        SyncObjectVars(name);
        InvalidateGameWindow();
    }

    static void ExecRotateBy(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^поверн(уть|и)\s+", "", RegexOptions.IgnoreCase).Trim();
        int onIdx = -1;
        var parts = SplitArgsPreservingQuotes(rest);
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Equals("на", StringComparison.OrdinalIgnoreCase)) { onIdx = i; break; }
        }
        if (onIdx < 1 || parts.Count < onIdx + 2)
            throw new Exception($"строка {line}: надо так -> повернуть мяч на 15");
        string name = parts[0];
        double deg = ToNum(EvalArith(parts[onIdx + 1], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.Angle = (obj.Angle + deg) % 360.0;
            if (obj.Angle < 0) obj.Angle += 360.0;
            obj.Props["угол"] = obj.Angle;
        }
        SyncObjectVars(name);
        InvalidateGameWindow();
    }

    static void ExecSetAngularVelocity(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+угловую\s+скорость\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> задать угловую скорость мяч 45");
        string name = parts[0];
        double degS = ToNum(EvalArith(parts[1], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.AngularVelocity = degS;
        }
        SyncObjectVars(name);
        StartPhysics();
    }

    static void ExecApplyForce(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^примени(ть|й)?\s+силу\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 3) throw new Exception($"строка {line}: надо так -> применить силу мяч 100 -50");
        string name = parts[0];
        double fx = ToNum(EvalArith(parts[1], line), line);
        double fy = ToNum(EvalArith(parts[2], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            double m = obj.Mass > 0 ? obj.Mass : 1.0;
            obj.VelocityX += fx / m;
            obj.VelocityY += fy / m;
        }
        SyncObjectVars(name);
        StartPhysics();
    }

    static void ExecApplyExplosion(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^примени(ть|й)?\s+взрыв\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 4) throw new Exception($"строка {line}: надо так -> применить взрыв 400 300 500 200");
        double ex = ToNum(EvalArith(parts[0], line), line);
        double ey = ToNum(EvalArith(parts[1], line), line);
        double force = ToNum(EvalArith(parts[2], line), line);
        double radius = ToNum(EvalArith(parts[3], line), line);
        lock (GameObjectsLock)
        {
            foreach (var obj in GameObjects.Values)
            {
                if (obj.Motion != ObjectMotion.Dynamic || !obj.Visible) continue;
                var b = obj.GetBounds();
                double cx = b.X + b.Width / 2.0;
                double cy = b.Y + b.Height / 2.0;
                double dx = cx - ex;
                double dy = cy - ey;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < radius && dist > 1e-4)
                {
                    double factor = (1.0 - dist / radius) * force;
                    double nx = dx / dist;
                    double ny = dy / dist;
                    double m = obj.Mass > 0 ? obj.Mass : 1.0;
                    obj.VelocityX += (nx * factor) / m;
                    obj.VelocityY += (ny * factor) / m;
                }
            }
        }
        StartPhysics();
    }

    static void ExecAttractTo(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^притян(уть|и)\s+", "", RegexOptions.IgnoreCase).Trim();
        int kIdx = -1;
        var parts = SplitArgsPreservingQuotes(rest);
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Equals("к", StringComparison.OrdinalIgnoreCase)) { kIdx = i; break; }
        }
        if (kIdx < 1 || parts.Count < kIdx + 4)
            throw new Exception($"строка {line}: надо так -> притянуть мяч к 400 300 50");
        string name = parts[0];
        double tx = ToNum(EvalArith(parts[kIdx + 1], line), line);
        double ty = ToNum(EvalArith(parts[kIdx + 2], line), line);
        double force = ToNum(EvalArith(parts[kIdx + 3], line), line);
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(name, out var obj) && obj.Motion == ObjectMotion.Dynamic)
            {
                var b = obj.GetBounds();
                double cx = b.X + b.Width / 2.0;
                double cy = b.Y + b.Height / 2.0;
                double dx = tx - cx;
                double dy = ty - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > 1e-4)
                {
                    double nx = dx / dist;
                    double ny = dy / dist;
                    double m = obj.Mass > 0 ? obj.Mass : 1.0;
                    obj.VelocityX += (nx * force) / m;
                    obj.VelocityY += (ny * force) / m;
                }
            }
        }
        StartPhysics();
    }

    static void ExecSetMass(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+масс[ую]\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2 || !IsPhysicsTarget(parts[0]))
        {
            Exception? assignErr = null;
            try { ExecSetGeneric("задать массу " + rest, line); return; }
            catch (Exception ex) { assignErr = ex; }
            if (parts.Count == 0 || !NameRegex.IsMatch(parts[0]) || IsBoolWord(parts[0]))
                throw assignErr!;
        }
        string name = parts[0];
        double mass = ToNum(EvalArith(parts[1], line), line);
        if (mass <= 0) mass = 0.1;
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.Mass = mass;
            obj.Props["масса"] = mass;
        }
        SyncObjectVars(name);
    }

    static void ExecSetDamping(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+демпфирование\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2 || !IsPhysicsTarget(parts[0]))
        {
            Exception? assignErr = null;
            try { ExecSetGeneric("задать демпфирование " + rest, line); return; }
            catch (Exception ex) { assignErr = ex; }
            if (parts.Count == 0 || !NameRegex.IsMatch(parts[0]) || IsBoolWord(parts[0]))
                throw assignErr!;
        }
        string name = parts[0];
        double damping = ToNum(EvalArith(parts[1], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.Damping = Math.Clamp(damping, 0.0, 10.0);
            obj.Props["демпфирование"] = obj.Damping;
        }
        SyncObjectVars(name);
    }

    static void ExecSetElasticity(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+упругость\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2 || !IsPhysicsTarget(parts[0]))
        {
            Exception? assignErr = null;
            try { ExecSetGeneric("задать упругость " + rest, line); return; }
            catch (Exception ex) { assignErr = ex; }
            if (parts.Count == 0 || !NameRegex.IsMatch(parts[0]) || IsBoolWord(parts[0]))
                throw assignErr!;
        }
        string name = parts[0];
        double elast = ToNum(EvalArith(parts[1], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.Elasticity = Math.Clamp(elast, 0.0, 1.0);
            obj.Props["упругость"] = obj.Elasticity;
        }
        SyncObjectVars(name);
    }

    static void ExecSetFriction(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+трение\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2 || !IsPhysicsTarget(parts[0]))
        {
            Exception? assignErr = null;
            try { ExecSetGeneric("задать трение " + rest, line); return; }
            catch (Exception ex) { assignErr = ex; }
            if (parts.Count == 0 || !NameRegex.IsMatch(parts[0]) || IsBoolWord(parts[0]))
                throw assignErr!;
        }
        string name = parts[0];
        double f = ToNum(EvalArith(parts[1], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.Friction = Math.Clamp(f, 0.0, 1.0);
            obj.Props["трение"] = obj.Friction;
        }
        SyncObjectVars(name);
    }

    static void ExecSetMaxSpeed(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+лимит\s+скорости\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> задать лимит скорости мяч 200");
        string name = parts[0];
        double spd = ToNum(EvalArith(parts[1], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.MaxSpeed = spd > 0 ? spd : double.MaxValue;
            obj.Props["лимит_скорости"] = obj.MaxSpeed;
        }
        SyncObjectVars(name);
    }

    static void ExecFreezeX(string text, int line)
    {
        string name = Regex.Replace(text.Trim(), @"^заморо(зить|зь)\s+[хx]\b", "", RegexOptions.IgnoreCase).Trim();
        if (name == "") throw new Exception($"строка {line}: надо так -> заморозить х мяч");
        name = SplitArgsPreservingQuotes(name).FirstOrDefault() ?? "";
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.FreezeX = true;
            obj.VelocityX = 0;
        }
        SyncObjectVars(name);
    }

    static void ExecFreezeY(string text, int line)
    {
        string name = Regex.Replace(text.Trim(), @"^заморо(зить|зь)\s+[уy]\b", "", RegexOptions.IgnoreCase).Trim();
        if (name == "") throw new Exception($"строка {line}: надо так -> заморозить у мяч");
        name = SplitArgsPreservingQuotes(name).FirstOrDefault() ?? "";
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.FreezeY = true;
            obj.VelocityY = 0;
        }
        SyncObjectVars(name);
    }

    static void ExecUnfreeze(string text, int line)
    {
        string name = Regex.Replace(text.Trim(), @"^разморо(зить|зь)\b", "", RegexOptions.IgnoreCase).Trim();
        if (name == "") throw new Exception($"строка {line}: надо так -> разморозить мяч");
        name = SplitArgsPreservingQuotes(name).FirstOrDefault() ?? "";
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.FreezeX = false;
            obj.FreezeY = false;
        }
        SyncObjectVars(name);
    }

    static void ExecSetConstraintX(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+ограничение\s+[хx]\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 3) throw new Exception($"строка {line}: надо так -> задать ограничение х мяч 0 800");
        string name = parts[0];
        double minX = ToNum(EvalArith(parts[1], line), line);
        double maxX = ToNum(EvalArith(parts[2], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.ConstraintMinX = minX;
            obj.ConstraintMaxX = maxX;
        }
        SyncObjectVars(name);
    }

    static void ExecSetConstraintY(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+ограничение\s+[уy]\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 3) throw new Exception($"строка {line}: надо так -> задать ограничение у мяч 0 600");
        string name = parts[0];
        double minY = ToNum(EvalArith(parts[1], line), line);
        double maxY = ToNum(EvalArith(parts[2], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.ConstraintMinY = minY;
            obj.ConstraintMaxY = maxY;
        }
        SyncObjectVars(name);
    }

    static void ExecAttachTo(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^прикреп(ить|и)\s+", "", RegexOptions.IgnoreCase).Trim();
        int kIdx = -1;
        var parts = SplitArgsPreservingQuotes(rest);
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Equals("к", StringComparison.OrdinalIgnoreCase)) { kIdx = i; break; }
        }
        if (kIdx < 1 || parts.Count < kIdx + 2)
            throw new Exception($"строка {line}: надо так -> прикрепить шапка к игрок");
        string childName = parts[0];
        string parentName = parts[kIdx + 1];
        double ox = parts.Count >= kIdx + 4 ? ToNum(EvalArith(parts[kIdx + 2], line), line) : 0;
        double oy = parts.Count >= kIdx + 4 ? ToNum(EvalArith(parts[kIdx + 3], line), line) : 0;
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(childName, out var child))
                GameObjects[childName] = child = new GameObject { Name = childName };
            child.AttachedTo = parentName;
            child.AttachOffsetX = ox;
            child.AttachOffsetY = oy;
        }
        StartPhysics();
    }

    static void ExecDetach(string text, int line)
    {
        string name = Regex.Replace(text.Trim(), @"^откреп(ить|и)\b", "", RegexOptions.IgnoreCase).Trim();
        if (name == "") throw new Exception($"строка {line}: надо так -> открепить шапка");
        name = SplitArgsPreservingQuotes(name).FirstOrDefault() ?? "";
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(name, out var child))
                child.AttachedTo = null;
        }
    }

    static void ExecSetCollisionLayer(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+слой\s+колли[зж]ий\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> задать слой коллизий мяч 1");
        string name = parts[0];
        int layer = (int)Math.Round(ToNum(EvalArith(parts[1], line), line));
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.CollisionLayer = layer;
        }
        SyncObjectVars(name);
    }

    static void ExecSetCollisionMask(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^маска\s+колли[зж]ий\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> маска коллизий мяч 255");
        string name = parts[0];
        int mask = (int)Math.Round(ToNum(EvalArith(parts[1], line), line));
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.CollisionMask = mask;
        }
        SyncObjectVars(name);
    }

    static void ExecIgnoreCollision(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^игнорировать\s+колли[зж]ии\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> игнорировать коллизии пуля игрок");
        string a = parts[0];
        string b = parts[1];
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(a, out var oa)) oa.IgnoredCollisions.Add(b);
            if (GameObjects.TryGetValue(b, out var ob)) ob.IgnoredCollisions.Add(a);
        }
    }

    static void ExecRestoreCollision(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^восстановить\s+колли[зж]ии\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> восстановить коллизии пуля игрок");
        string a = parts[0];
        string b = parts[1];
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(a, out var oa)) oa.IgnoredCollisions.Remove(b);
            if (GameObjects.TryGetValue(b, out var ob)) ob.IgnoredCollisions.Remove(a);
        }
    }

    static void ExecSceneBorder(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^граница\s+сцены\b", "", RegexOptions.IgnoreCase).Trim().ToLowerInvariant();
        SceneBorderEnabled = (rest is "вкл" or "да" or "1" or "true" or "истина");
        EnsureGameWindow();
        StartPhysics();
    }

    static void ExecBounceOffEdge(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^отско(чить|чи)\s+", "", RegexOptions.IgnoreCase).Trim();
        int otIdx = -1;
        var parts = SplitArgsPreservingQuotes(rest);
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Equals("от", StringComparison.OrdinalIgnoreCase)) { otIdx = i; break; }
        }
        if (otIdx < 1) throw new Exception($"строка {line}: надо так -> отскочить мяч от края");
        string name = parts[0];
        int winW = GetCurrentWindowWidth();
        int winH = GetCurrentWindowHeight();
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(name, out var obj))
            {
                var b = obj.GetBounds();
                if (obj.X <= 0 || obj.X + b.Width >= winW)
                    obj.VelocityX = -obj.VelocityX;
                if (obj.Y <= 0 || obj.Y + b.Height >= winH)
                    obj.VelocityY = -obj.VelocityY;
            }
        }
        SyncObjectVars(name);
    }

    static void ExecSmoothMoveTo(string text, int line)
    {
        var m = Regex.Match(text.Trim(), @"^плавно\s+переместить\s+(\S+)\s+в\s+(.+?)\s+(.+?)\s+за\s+(.+?)$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new Exception($"строка {line}: надо так -> плавно переместить мяч в 200 300 за 1 секунда");
        string name = m.Groups[1].Value.Trim();
        double tx = ToNum(EvalArith(m.Groups[2].Value.Trim(), line), line);
        double ty = ToNum(EvalArith(m.Groups[3].Value.Trim(), line), line);
        double durSec = ParseEverySec(m.Groups[4].Value.Trim(), line);
        if (durSec <= 0) durSec = 0.5;

        EnsureGameWindow();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            double startX, startY;
            lock (GameObjectsLock)
            {
                if (!GameObjects.TryGetValue(name, out var obj)) return;
                startX = obj.X;
                startY = obj.Y;
            }
            int steps = (int)Math.Max(10, durSec * 60);
            int sleepMs = (int)(durSec * 1000 / steps);
            for (int s = 1; s <= steps; s++)
            {
                double t = (double)s / steps;
                double ease = t * t * (3 - 2 * t);
                lock (GameObjectsLock)
                {
                    if (GameObjects.TryGetValue(name, out var obj))
                    {
                        obj.X = startX + (tx - startX) * ease;
                        obj.Y = startY + (ty - startY) * ease;
                    }
                }
                SyncObjectVars(name);
                InvalidateGameWindow();
                Thread.Sleep(sleepMs);
            }
        });
    }

    static void ExecGetDistance(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^расстояние\b", "", RegexOptions.IgnoreCase).Trim();
        if (!SplitTargetVar(rest, out string beforeArrow, out string varName) || string.IsNullOrWhiteSpace(varName))
            throw new Exception($"строка {line}: надо так -> расстояние мяч цель → d");
        var parts = SplitArgsPreservingQuotes(beforeArrow);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> расстояние мяч цель → d");
        string n1 = parts[0];
        string n2 = parts[1];
        double dist = 0;
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(n1, out var o1) && GameObjects.TryGetValue(n2, out var o2))
            {
                var b1 = o1.GetBounds();
                var b2 = o2.GetBounds();
                double c1x = b1.X + b1.Width / 2.0, c1y = b1.Y + b1.Height / 2.0;
                double c2x = b2.X + b2.Width / 2.0, c2y = b2.Y + b2.Height / 2.0;
                double dx = c2x - c1x, dy = c2y - c1y;
                dist = Math.Sqrt(dx * dx + dy * dy);
            }
        }
        Vars[varName] = NormNum(dist);
    }

    static void ExecGetAngleTo(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^угол\s+к\b", "", RegexOptions.IgnoreCase).Trim();
        if (!SplitTargetVar(rest, out string beforeArrow, out string varName) || string.IsNullOrWhiteSpace(varName))
            throw new Exception($"строка {line}: надо так -> угол к мяч цель → a");
        var parts = SplitArgsPreservingQuotes(beforeArrow);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> угол к мяч цель → a");
        string n1 = parts[0];
        string n2 = parts[1];
        double deg = 0;
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(n1, out var o1) && GameObjects.TryGetValue(n2, out var o2))
            {
                var b1 = o1.GetBounds();
                var b2 = o2.GetBounds();
                double c1x = b1.X + b1.Width / 2.0, c1y = b1.Y + b1.Height / 2.0;
                double c2x = b2.X + b2.Width / 2.0, c2y = b2.Y + b2.Height / 2.0;
                double dx = c2x - c1x, dy = c2y - c1y;
                deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                if (deg < 0) deg += 360.0;
            }
        }
        Vars[varName] = NormNum(deg);
    }

    static void ExecCheckCollision(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^проверить\s+столкновение\b", "", RegexOptions.IgnoreCase).Trim();
        if (!SplitTargetVar(rest, out string beforeArrow, out string varName) || string.IsNullOrWhiteSpace(varName))
            throw new Exception($"строка {line}: надо так -> проверить столкновение мяч стена → попал");
        var parts = SplitArgsPreservingQuotes(beforeArrow);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> проверить столкновение мяч стена → попал");
        string n1 = parts[0];
        string n2 = parts[1];
        bool hit = false;
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(n1, out var o1) && GameObjects.TryGetValue(n2, out var o2) && o1.Visible && o2.Visible)
            {
                var b1 = o1.GetBounds();
                var b2 = o2.GetBounds();
                hit = BoxesIntersect(b1.X, b1.Y, b1.Width, b1.Height, b2.X, b2.Y, b2.Width, b2.Height);
            }
        }
        Vars[varName] = hit;
    }

    static void ExecObjectOnScreen(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^об[ъе]кт\s+на\s+экране\b", "", RegexOptions.IgnoreCase).Trim();
        if (!SplitTargetVar(rest, out string beforeArrow, out string varName) || string.IsNullOrWhiteSpace(varName))
            throw new Exception($"строка {line}: надо так -> объект на экране мяч → видно");
        string name = SplitArgsPreservingQuotes(beforeArrow).FirstOrDefault() ?? "";
        bool onScreen = false;
        int winW = GetCurrentWindowWidth();
        int winH = GetCurrentWindowHeight();
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(name, out var o) && o.Visible)
            {
                var b = o.GetBounds();
                onScreen = !(b.X + b.Width < 0 || b.X > winW || b.Y + b.Height < 0 || b.Y > winH);
            }
        }
        Vars[varName] = onScreen;
    }

    static void ExecGetSpeed(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^скорость\b", "", RegexOptions.IgnoreCase).Trim();
        if (!SplitTargetVar(rest, out string beforeArrow, out string varName) || string.IsNullOrWhiteSpace(varName))
            throw new Exception($"строка {line}: надо так -> скорость мяч → v");
        string name = SplitArgsPreservingQuotes(beforeArrow).FirstOrDefault() ?? "";
        double spd = 0;
        lock (GameObjectsLock)
        {
            if (GameObjects.TryGetValue(name, out var o))
                spd = o.Speed;
        }
        Vars[varName] = NormNum(spd);
    }

    static void ExecSetDirection(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+направление\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2 || !IsPhysicsTarget(parts[0]))
        {
            Exception? assignErr = null;
            try { ExecSetGeneric("задать направление " + rest, line); return; }
            catch (Exception ex) { assignErr = ex; }
            if (parts.Count == 0 || !NameRegex.IsMatch(parts[0]) || IsBoolWord(parts[0]))
                throw assignErr!;
        }
        string name = parts[0];
        double deg = ToNum(EvalArith(parts[1], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            obj.Angle = deg % 360.0;
            if (obj.Angle < 0) obj.Angle += 360.0;
            obj.Props["угол"] = obj.Angle;
            double spd = obj.Speed;
            if (spd > 1e-6)
            {
                double rad = obj.Angle * Math.PI / 180.0;
                obj.VelocityX = Math.Cos(rad) * spd;
                obj.VelocityY = Math.Sin(rad) * spd;
            }
        }
        SyncObjectVars(name);
        InvalidateGameWindow();
    }

    static void ExecSetSpeedByDirection(string text, int line)
    {
        string rest = Regex.Replace(text.Trim(), @"^зада(ть|й)\s+скорость\s+по\s+направлению\b", "", RegexOptions.IgnoreCase).Trim();
        var parts = SplitArgsPreservingQuotes(rest);
        if (parts.Count < 2) throw new Exception($"строка {line}: надо так -> задать скорость по направлению мяч 50");
        string name = parts[0];
        double spd = ToNum(EvalArith(parts[1], line), line);
        EnsureGameWindow();
        lock (GameObjectsLock)
        {
            if (!GameObjects.TryGetValue(name, out var obj))
                GameObjects[name] = obj = new GameObject { Name = name };
            double rad = obj.Angle * Math.PI / 180.0;
            obj.VelocityX = Math.Cos(rad) * spd;
            obj.VelocityY = Math.Sin(rad) * spd;
        }
        SyncObjectVars(name);
        StartPhysics();
    }

    static string curDir = "";

    static void RunFile(string path)
    {
        StopPhysics();
        SceneBorderEnabled = false;
        lock (PhysicsCollisionHandlers) { PhysicsCollisionHandlers.Clear(); }
        lock (PhysicsOffScreenHandlers) { PhysicsOffScreenHandlers.Clear(); }
        lock (PhysicsRestHandlers) { PhysicsRestHandlers.Clear(); }
        curDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory;
        lock (ClickHandlers) { ClickHandlers.Clear(); }
        Vars.Clear(); Handlers.Clear(); OnStarts.Clear(); Triggers.Clear(); KeyHandlers.Clear(); GameObjects.Clear();
        BroadcastDepth = 0; TriggerDepth = 0;
        GameHostService.Current.CloseWindow();
        var code = LoadWithIncludes(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        CollectHandlers(code);
        CollectStarts(code);
        CollectTriggers(code);
        CollectKeyHandlers(code);
        CollectPhysicsHandlers(code);
        if ((KeyHandlers.Count > 0 || ClickHandlers.Count > 0) && !GameHostService.Current.IsActive)
        {
            EnsureGameWindow();
        }
        foreach (var body in OnStarts)
            ExecRange(body, 0, body.Count);
        ExecRange(code, 0, code.Count);
        if (code.Count > 0) CheckTriggers(code[^1].No);
        if (GameHostService.Current.IsActive)
        {
            Console.WriteLine("[окно ждет закрытия...]");
            GameHostService.Current.WaitUntilClosed();
        }
    }

    static int Main(string[] args)
    {
        try
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            try { Console.SetOut(TextWriter.Null); } catch { }
            try { Console.SetError(TextWriter.Null); } catch { }
        }

        string baseDir = AppContext.BaseDirectory;
        var asm = typeof(Program).Assembly;
        Stream? bundleStream = asm.GetManifestResourceStream("GameBundle.zip");
        if (bundleStream == null)
        {
            var names = asm.GetManifestResourceNames();
            var match = Array.Find(names, n => n.EndsWith("GameBundle.zip", StringComparison.OrdinalIgnoreCase));
            if (match != null) bundleStream = asm.GetManifestResourceStream(match);
        }
        if (bundleStream != null)
        {
            try
            {
                string tempGameDir = Path.Combine(Path.GetTempPath(), "NcodeGame_" + Math.Abs(AppDomain.CurrentDomain.FriendlyName.GetHashCode()).ToString("X"));
                Directory.CreateDirectory(tempGameDir);
                string tempDirFullPath = Path.GetFullPath(tempGameDir);
                if (!tempDirFullPath.EndsWith(Path.DirectorySeparatorChar))
                    tempDirFullPath += Path.DirectorySeparatorChar;

                using (var archive = new ZipArchive(bundleStream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string destPath = Path.GetFullPath(Path.Combine(tempGameDir, entry.FullName));
                        if (!destPath.StartsWith(tempDirFullPath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string? d = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                        entry.ExtractToFile(destPath, true);
                    }
                }
                baseDir = tempGameDir;
                Environment.CurrentDirectory = tempGameDir;
            }
            catch (Exception ex) { Console.Error.WriteLine("[бандл] " + ex.Message); }
        }

        string configPath = Path.Combine(baseDir, "game.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath, Encoding.UTF8);
                ActiveConfig = System.Text.Json.JsonSerializer.Deserialize<GameConfig>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex) { Console.Error.WriteLine("[конфиг] " + ex.Message); }
        }

        string path;
        if (args.Length > 0) path = args[0];
        else if (ActiveConfig?.Main != null && File.Exists(Path.Combine(baseDir, ActiveConfig.Main))) path = Path.Combine(baseDir, ActiveConfig.Main);
        else if (File.Exists(Path.Combine(baseDir, "main.ncode"))) path = Path.Combine(baseDir, "main.ncode");
        else if (File.Exists(Path.Combine(baseDir, "game.ncode"))) path = Path.Combine(baseDir, "game.ncode");
        else if (File.Exists("main.ncode")) path = "main.ncode";
        else if (File.Exists("game.ncode")) path = "game.ncode";
        else if (File.Exists("test.ncode")) path = "test.ncode";
        else if (File.Exists("../test.ncode")) path = "../test.ncode";
        else path = "main.ncode";

        try
        {
            try { RunFile(path); return 0; }
            catch (BreakException) { Console.WriteLine("Ошибка: 'остановить' без цикла"); return 1; }
            catch (ContinueException) { Console.WriteLine("Ошибка: 'продолжить' без цикла"); return 1; }
            catch (ExitException) { GameHostService.Current.CloseWindow(); return 0; }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка: " + ex.Message);
#if WINDOWS
                try
                {
                    MessageBox.Show("Ошибка выполнения игры: " + ex.Message, ActiveConfig?.Title ?? "Ncode", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
#endif
                return 1;
            }
        }
        finally
        {
            StopPhysics();
            StopAllAudio();
        }
    }

#if ANDROID
    public static void RunAndroid(Android.Content.Context context, IGameHost host)
    {
        GameHostService.Current = host;
        try { Console.InputEncoding = Encoding.UTF8; Console.OutputEncoding = Encoding.UTF8; } catch { }
        string baseDir = context.FilesDir?.AbsolutePath ?? AppContext.BaseDirectory;
        Stream? bundleStream = null;
        try
        {
            bundleStream = context.Assets?.Open("GameBundle.zip");
            if (bundleStream == null)
            {
                var asm = typeof(Program).Assembly;
                bundleStream = asm.GetManifestResourceStream("GameBundle.zip");
                if (bundleStream == null)
                {
                    var names = asm.GetManifestResourceNames();
                    var match = Array.Find(names, n => n.EndsWith("GameBundle.zip", StringComparison.OrdinalIgnoreCase));
                    if (match != null) bundleStream = asm.GetManifestResourceStream(match);
                }
            }
        }
        catch { }
        if (bundleStream != null)
        {
            try
            {
                string tempGameDir = Path.Combine(context.FilesDir?.AbsolutePath ?? Path.GetTempPath(), "NcodeGame");
                Directory.CreateDirectory(tempGameDir);
                string tempDirFullPath = Path.GetFullPath(tempGameDir);
                if (!tempDirFullPath.EndsWith(Path.DirectorySeparatorChar)) tempDirFullPath += Path.DirectorySeparatorChar;
                using (var archive = new ZipArchive(bundleStream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string destPath = Path.GetFullPath(Path.Combine(tempGameDir, entry.FullName));
                        if (!destPath.StartsWith(tempDirFullPath, StringComparison.OrdinalIgnoreCase)) continue;
                        string? d = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                        entry.ExtractToFile(destPath, true);
                    }
                }
                baseDir = tempGameDir;
                curDir = tempGameDir;
                Environment.CurrentDirectory = tempGameDir;
            }
            catch (Exception ex) { Console.Error.WriteLine("[бандл] " + ex.Message); }
        }
        else
        {
            curDir = baseDir;
        }

        string configPath = Path.Combine(baseDir, "game.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath, Encoding.UTF8);
                ActiveConfig = System.Text.Json.JsonSerializer.Deserialize<GameConfig>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex) { Console.Error.WriteLine("[конфиг] " + ex.Message); }
        }

        string path;
        if (ActiveConfig?.Main != null && File.Exists(Path.Combine(baseDir, ActiveConfig.Main))) path = Path.Combine(baseDir, ActiveConfig.Main);
        else if (File.Exists(Path.Combine(baseDir, "main.ncode"))) path = Path.Combine(baseDir, "main.ncode");
        else if (File.Exists(Path.Combine(baseDir, "game.ncode"))) path = Path.Combine(baseDir, "game.ncode");
        else path = Path.Combine(baseDir, "main.ncode");

        try
        {
            RunFile(path);
            if (GameHostService.Current.IsActive) GameHostService.Current.WaitUntilClosed();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка: " + ex.Message);
        }
        finally
        {
            StopPhysics();
            StopAllAudio();
        }
    }
#endif
}

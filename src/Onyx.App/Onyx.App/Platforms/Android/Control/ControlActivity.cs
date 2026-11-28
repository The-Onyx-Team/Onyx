using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Onyx.App.UsageData;
using Onyx.Collector.Core.Control;
using Onyx.Collector.Core.Crypto;
using Color = Android.Graphics.Color;
using View = Android.Views.View;
using Button = Android.Widget.Button;

namespace Onyx.App.Control;

/// <summary>
/// The question shown before an app opens (Stay in control mode). A correct answer closes it and the app is
/// usable for the grace period; "Not now" and Back go to the home screen. Plain Android views, so it appears at
/// once without starting the Blazor UI.
/// </summary>
[Activity(Name = "onyx.control.ControlActivity", Theme = "@android:style/Theme.Material.NoActionBar", ExcludeFromRecents = true, TaskAffinity = "",
    LaunchMode = LaunchMode.SingleInstance, NoHistory = true, Exported = false,
    WindowSoftInputMode = SoftInput.AdjustResize | SoftInput.StateVisible)]
public class ControlActivity : Activity
{
    public const string PackageExtra = "onyx.package";

    private static readonly Color s_Background = Color.ParseColor("#0B0C0E");
    private static readonly Color s_Text = Color.ParseColor("#F6F2E9");
    private static readonly Color s_Muted = Color.ParseColor("#9AA0A8");
    private static readonly Color s_Accent = Color.ParseColor("#C58848");

    private readonly Random m_Random = new();
    private ControlSettings m_Settings = new();
    private ControlQuestion m_Question = new("", "");
    private string m_Package = "";
    private TextView m_App = null!;
    private TextView m_QuestionView = null!;
    private TextView m_Feedback = null!;
    private EditText m_Answer = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Android 15+ draws apps edge to edge, under transparent bars; before, the bars get the screen's colour.
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            Window?.SetStatusBarColor(s_Background);
            Window?.SetNavigationBarColor(s_Background);
        }
        SetContentView(BuildLayout());
        Show(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Show(intent);
    }

    private void Show(Intent? intent)
    {
        m_Package = intent?.GetStringExtra(PackageExtra) ?? "";
        m_Settings = AndroidReminders.ReadSavedSettings()?.Control ?? new ControlSettings();
        m_App.Text = $"Before you open {AndroidUsageReader.Describe(m_Package).Name}:";
        NextQuestion();
    }

    private void NextQuestion()
    {
        m_Question = StayInControl.NextQuestion(m_Settings, m_Random);
        m_QuestionView.Text = m_Question.Question;
        m_Answer.Text = "";
        m_Answer.RequestFocus();
    }

    private void Submit()
    {
        if (StayInControl.IsCorrect(m_Question, m_Answer.Text))
        {
            StayInControlService.Gate.Grant(m_Settings, "android:" + m_Package);
            Finish();
            return;
        }

        m_Feedback.Text = "Not quite. Try this one:";
        NextQuestion();
    }

    private void GoHome()
    {
        StartActivity(new Intent(Intent.ActionMain).AddCategory(Intent.CategoryHome).SetFlags(ActivityFlags.NewTask));
        Finish();
    }

#pragma warning disable CA1422, CS0672, CS0618 // Back goes home on every version; predictive back is not enabled.
    public override void OnBackPressed() => GoHome();
#pragma warning restore CA1422, CS0672, CS0618

    private View BuildLayout()
    {
        var density = Resources!.DisplayMetrics!.Density;
        int Dp(int dp) => (int)(dp * density);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(s_Background);
        root.SetGravity(GravityFlags.Center);
        root.SetPadding(Dp(28), Dp(48), Dp(28), Dp(48));

        TextView Text(float size, Color color, TypefaceStyle style = TypefaceStyle.Normal)
        {
            var view = new TextView(this) { TextSize = size, Gravity = GravityFlags.Center };
            view.SetTextColor(color);
            view.SetTypeface(Typeface.Default, style);
            root.AddView(view, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                BottomMargin = Dp(12)
            });
            return view;
        }

        var title = Text(14, s_Accent, TypefaceStyle.Bold);
        title.Text = "STAY IN CONTROL";
        m_App = Text(16, s_Muted);
        m_QuestionView = Text(28, s_Text, TypefaceStyle.Bold);
        m_Feedback = Text(14, s_Accent);

        m_Answer = new EditText(this) { Gravity = GravityFlags.Center, TextSize = 22 };
        m_Answer.SetSingleLine(true);
        m_Answer.SetTextColor(s_Text);
        m_Answer.SetHintTextColor(s_Muted);
        m_Answer.Hint = "Your answer";
        m_Answer.ImeOptions = ImeAction.Done;
        m_Answer.InputType = InputTypes.ClassText | InputTypes.TextFlagNoSuggestions;
        m_Answer.EditorAction += (_, e) =>
        {
            Submit();
            e.Handled = true;
        };
        root.AddView(m_Answer, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = Dp(20)
        });

        var open = new Button(this) { Text = "Open" };
        open.SetTextColor(s_Background);
        open.SetBackgroundColor(s_Accent);
        open.Click += (_, _) => Submit();
        root.AddView(open, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(52)) { BottomMargin = Dp(12) });

        var home = new Button(this) { Text = "Not now" };
        home.SetTextColor(s_Text);
        home.SetBackgroundColor(Color.Transparent);
        home.Click += (_, _) => GoHome();
        root.AddView(home, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(52)));

        return root;
    }
}

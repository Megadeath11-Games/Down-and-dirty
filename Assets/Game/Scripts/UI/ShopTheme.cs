using UnityEngine;

namespace FPSBase.UI
{
    /// <summary>
    /// The look of the shops and panels: dark earth-brown panels, gold trim and headings, cream text, green to buy.
    /// Rounded boxes are small textures drawn as 9-slices. Built on first use inside OnGUI.
    /// </summary>
    public static class ShopTheme
    {
        public static readonly Color Deep = new(0.115f, 0.082f, 0.055f, 0.97f);
        public static readonly Color Card = new(0.205f, 0.150f, 0.098f, 1f);
        public static readonly Color CardHover = new(0.285f, 0.210f, 0.125f, 1f);
        public static readonly Color Gold = new(0.965f, 0.705f, 0.165f, 1f);
        public static readonly Color GoldDim = new(0.70f, 0.52f, 0.16f, 1f);
        public static readonly Color Cream = new(0.955f, 0.900f, 0.780f, 1f);
        public static readonly Color Dim = new(0.730f, 0.650f, 0.520f, 1f);
        public static readonly Color Green = new(0.290f, 0.560f, 0.235f, 1f);
        public static readonly Color GreenHover = new(0.345f, 0.665f, 0.280f, 1f);
        public static readonly Color GreenDown = new(0.225f, 0.450f, 0.185f, 1f);
        public static readonly Color GreenText = new(0.560f, 0.880f, 0.450f, 1f);
        public static readonly Color Red = new(0.900f, 0.460f, 0.340f, 1f);

        static GUISkin skin;
        public static Texture2D TexHud, TexToast, TexDeep, TexCard, TexCardHover, TexCardSel, TexGreen, TexGreenHover, TexGreenDown, TexGrey, TexTab, TexTabOn, TexField, TexWhite;
        public static GUIStyle Toolbar, Toast, Title, Heading, Body, Small, DimStyle, Price, PriceBad, Money, CardName, Tab, TabOn, BuyButton, PlainButton, SpecLabel, SpecValue;

        /// <summary>The themed skin (a copy of the default one). Call inside OnGUI.</summary>
        public static GUISkin Skin
        {
            get
            {
                if (skin != null) return skin;
                Build();
                return skin;
            }
        }

        static void Build()
        {
            TexWhite = Texture2D.whiteTexture;
            TexDeep = Round(Deep, GoldDim, 6, 2);
            TexHud = Round(new Color(0.10f, 0.072f, 0.048f, 0.84f), new Color(0.55f, 0.41f, 0.14f, 0.9f), 6, 1);
            TexToast = Round(new Color(0.13f, 0.095f, 0.06f, 0.93f), Gold, 6, 1);
            TexCard = Round(Card, new Color(0.34f, 0.26f, 0.15f, 1f), 6, 1);
            TexCardHover = Round(CardHover, GoldDim, 6, 1);
            TexCardSel = Round(CardHover, Gold, 6, 2);
            TexGreen = Round(Green, new Color(0.18f, 0.36f, 0.14f, 1f), 6, 1);
            TexGreenHover = Round(GreenHover, new Color(0.62f, 0.85f, 0.40f, 1f), 6, 1);
            TexGreenDown = Round(GreenDown, new Color(0.15f, 0.30f, 0.12f, 1f), 6, 1);
            TexGrey = Round(new Color(0.30f, 0.27f, 0.23f, 1f), new Color(0.22f, 0.20f, 0.17f, 1f), 6, 1);
            TexTab = Round(new Color(0.18f, 0.13f, 0.085f, 1f), new Color(0.30f, 0.23f, 0.13f, 1f), 6, 1);
            TexTabOn = Round(Gold, new Color(1f, 0.85f, 0.45f, 1f), 6, 1);
            TexField = Round(new Color(0.07f, 0.05f, 0.035f, 1f), GoldDim, 6, 1);

            skin = Object.Instantiate(GUI.skin);
            skin.name = "Shop theme";
            var b = new RectOffset(7, 7, 7, 7);
            void Text(GUIStyle st, Color c) { st.normal.textColor = c; st.hover.textColor = c; st.active.textColor = c; st.focused.textColor = c; st.onNormal.textColor = c; st.onHover.textColor = c; st.onActive.textColor = c; }

            Text(skin.label, Cream);
            skin.label.richText = true;
            skin.box.normal.background = TexCard; skin.box.border = b; Text(skin.box, Cream);
            skin.button.normal.background = TexGreen; skin.button.hover.background = TexGreenHover; skin.button.active.background = TexGreenDown;
            skin.button.focused.background = TexGreen; skin.button.onNormal.background = TexGreenDown; skin.button.onHover.background = TexGreenHover;
            skin.button.border = b; skin.button.fontStyle = FontStyle.Bold; skin.button.richText = true;
            Text(skin.button, new Color(0.97f, 0.97f, 0.90f, 1f));
            skin.toggle.richText = true; Text(skin.toggle, Cream);
            skin.textField.normal.background = TexField; skin.textField.focused.background = TexField; skin.textField.hover.background = TexField; skin.textField.border = new RectOffset(7, 7, 7, 7);
            Text(skin.textField, Cream);
            skin.verticalScrollbar.normal.background = Round(new Color(0.08f, 0.06f, 0.04f, 0.9f), new Color(0.08f, 0.06f, 0.04f, 0.9f), 6, 0);
            skin.verticalScrollbar.border = new RectOffset(7, 7, 7, 7);
            skin.verticalScrollbarThumb.normal.background = Round(GoldDim, Gold, 6, 1);
            skin.verticalScrollbarThumb.border = new RectOffset(7, 7, 7, 7);
            skin.horizontalScrollbar.normal.background = skin.verticalScrollbar.normal.background;
            // sliders: a dark groove and a gold knob
            skin.horizontalSlider.normal.background = Round(new Color(0.07f, 0.05f, 0.035f, 1f), GoldDim, 3, 1);
            skin.horizontalSlider.border = new RectOffset(3, 3, 3, 3);
            var knob = Round(Gold, new Color(1f, 0.88f, 0.5f, 1f), 4, 1);
            var knobHot = Round(new Color(1f, 0.82f, 0.35f, 1f), Color.white, 4, 1);
            skin.horizontalSliderThumb.normal.background = knob; skin.horizontalSliderThumb.hover.background = knobHot;
            skin.horizontalSliderThumb.active.background = knobHot; skin.horizontalSliderThumb.focused.background = knob;
            skin.horizontalSliderThumb.border = new RectOffset(4, 4, 4, 4);
            skin.horizontalScrollbarThumb.normal.background = skin.verticalScrollbarThumb.normal.background;

            Title = new GUIStyle(skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft }; Text(Title, Gold);
            Heading = new GUIStyle(skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = true }; Text(Heading, Cream);
            Body = new GUIStyle(skin.label) { fontSize = 14, wordWrap = true }; Text(Body, Cream);
            Small = new GUIStyle(skin.label) { fontSize = 12, wordWrap = true }; Text(Small, Cream);
            DimStyle = new GUIStyle(Small); Text(DimStyle, Dim);
            Price = new GUIStyle(skin.label) { fontSize = 16, fontStyle = FontStyle.Bold }; Text(Price, Gold);
            PriceBad = new GUIStyle(Price); Text(PriceBad, Red);
            Money = new GUIStyle(skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight }; Text(Money, GreenText);
            CardName = new GUIStyle(skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = true, clipping = TextClipping.Clip }; Text(CardName, Cream);
            Tab = new GUIStyle(skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 14, padding = new RectOffset(12, 8, 4, 4) };
            Tab.normal.background = TexTab; Tab.hover.background = TexCardHover; Tab.active.background = TexCardHover; Tab.focused.background = TexTab;
            Text(Tab, Cream);
            TabOn = new GUIStyle(Tab);
            TabOn.normal.background = TexTabOn; TabOn.hover.background = TexTabOn; TabOn.active.background = TexTabOn; TabOn.focused.background = TexTabOn;
            Text(TabOn, new Color(0.16f, 0.10f, 0.04f, 1f));
            BuyButton = new GUIStyle(skin.button) { fontSize = 18 };
            // a row of choices (tabs, settings): brown, the chosen one gold
            Toolbar = new GUIStyle(skin.button) { fontSize = 14, alignment = TextAnchor.MiddleCenter, margin = new RectOffset(2, 2, 2, 2) };
            Toolbar.normal.background = TexTab; Toolbar.hover.background = TexCardHover; Toolbar.active.background = TexCardHover; Toolbar.focused.background = TexTab;
            Toolbar.onNormal.background = TexTabOn; Toolbar.onHover.background = TexTabOn; Toolbar.onActive.background = TexTabOn; Toolbar.onFocused.background = TexTabOn;
            Text(Toolbar, Cream);
            var dark = new Color(0.16f, 0.10f, 0.04f, 1f);
            Toolbar.onNormal.textColor = dark; Toolbar.onHover.textColor = dark; Toolbar.onActive.textColor = dark; Toolbar.onFocused.textColor = dark;
            Toast = new GUIStyle(skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true, padding = new RectOffset(12, 12, 6, 6) };
            Toast.normal.background = TexToast; Text(Toast, Cream);
            PlainButton = new GUIStyle(skin.button) { fontSize = 14 };
            PlainButton.normal.background = TexTab; PlainButton.hover.background = TexCardHover; PlainButton.active.background = TexCard; PlainButton.focused.background = TexTab;
            Text(PlainButton, Cream);
            SpecLabel = new GUIStyle(skin.label) { fontSize = 13, wordWrap = true }; Text(SpecLabel, Dim);
            SpecValue = new GUIStyle(skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = true }; Text(SpecValue, Cream);
        }

        /// <summary>A rounded box: `fill`, a `border`-wide edge in `edge`, corner radius `r` (pixels of a 9-slice).</summary>
        static Texture2D Round(Color fill, Color edge, int r, int border)
        {
            int n = r * 2 + 2;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // distance inside the rounded rect (negative outside)
                    float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                    float d = r - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(d + 0.5f);
                    Color c = d < border ? Color.Lerp(fill, edge, Mathf.Clamp01(border - d + 0.5f)) : fill;
                    c.a *= a;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        /// <summary>Fill a rect with a colour.</summary>
        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
        }
    }
}

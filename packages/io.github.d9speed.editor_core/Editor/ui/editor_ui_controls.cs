using System;
using UnityEngine.UIElements;

namespace D9speed_BaseEditorUtils
{
    public static class EditorUiControls
    {
        public static Label Label(string text, string className = "d9_description")
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        public static VisualElement Header(string title, string description)
        {
            var header = new VisualElement();
            header.AddToClassList("d9_header");
            header.Add(Label(title, "d9_title"));
            header.Add(Label(description));
            return header;
        }

        public static VisualElement Section(VisualElement parent, string title)
        {
            var section = new VisualElement();
            section.AddToClassList("d9_section");
            var header = new VisualElement();
            header.AddToClassList("d9_section_header");
            header.Add(Label(title, "d9_section_title"));
            section.Add(header);
            parent.Add(section);
            return section;
        }

        public static VisualElement Row(bool wrap = true)
        {
            var row = new VisualElement();
            row.AddToClassList("d9_row");
            row.EnableInClassList("d9_row_wrap", wrap);
            return row;
        }

        public static Button Button(string text, Action action, bool primary = false)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("d9_button");
            button.EnableInClassList("d9_button_primary", primary);
            return button;
        }

        public static T Field<T>(T field) where T : VisualElement
        {
            field.AddToClassList("d9_field");
            return field;
        }

        public static Toggle Toggle(string text, bool value = false, Action<bool> changed = null)
        {
            var toggle = new Toggle { text = text, value = value };
            toggle.AddToClassList("d9_toggle");
            var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("d9_check_glyph");
            toggle.Q(className: UnityEngine.UIElements.Toggle.checkmarkUssClassName)?.Add(glyph);
            if (changed != null) toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
            return toggle;
        }

        public static Foldout Foldout(string title, bool expanded = false)
        {
            var foldout = new Foldout { text = title, value = expanded };
            foldout.AddToClassList("d9_foldout");
            return foldout;
        }
    }
}

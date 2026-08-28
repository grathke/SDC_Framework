Combo Checklist

1. Source values from database (no hardcoded business values).
2. Add "Make a Selection" as item 1.
3. Use standard names: ComboBox_FieldName and Label_FieldName.
4. Set both ValueMember and DisplayMember.
5. Validate required combos before save (placeholder is not valid).
6. Save the selected ValueMember to the mapped DB field.
7. Trim text inputs before save and enforce DB max lengths.
8. Keep combo binding/validation in shared patterns where possible.

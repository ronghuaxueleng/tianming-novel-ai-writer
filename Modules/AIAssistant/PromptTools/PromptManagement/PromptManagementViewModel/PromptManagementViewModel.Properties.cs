namespace TM.Modules.AIAssistant.PromptTools.PromptManagement;

public partial class PromptManagementViewModel
{
    private string _formName = string.Empty;
    public string FormName
    {
        get => _formName;
        set { _formName = value; OnPropertyChanged(); }
    }

    private string _formIcon = "Icon.Note";
    public string FormIcon
    {
        get => _formIcon;
        set { _formIcon = value; OnPropertyChanged(); }
    }

    private string _formStatus = "未启用";
    public string FormStatus
    {
        get => _formStatus;
        set { _formStatus = value; OnPropertyChanged(); }
    }

    private string _formCategory = string.Empty;
    private bool _suppressCategoryValueChanged;
    public string FormCategory
    {
        get => _formCategory;
        set
        {
            if (_formCategory != value)
            {
                _formCategory = value;
                OnPropertyChanged();

                if (!_suppressCategoryValueChanged)
                {
                    OnCategoryValueChanged(_formCategory);
                }
            }
        }
    }

    private string _formSystemPrompt = string.Empty;
    public string FormSystemPrompt
    {
        get => _formSystemPrompt;
        set { _formSystemPrompt = value; OnPropertyChanged(); }
    }

    private string _formUserTemplate = string.Empty;
    public string FormUserTemplate
    {
        get => _formUserTemplate;
        set { _formUserTemplate = value; OnPropertyChanged(); }
    }

    private string _formVariables = string.Empty;
    public string FormVariables
    {
        get => _formVariables;
        set { _formVariables = value; OnPropertyChanged(); }
    }

    private string _formTags = string.Empty;
    public string FormTags
    {
        get => _formTags;
        set { _formTags = value; OnPropertyChanged(); }
    }

    private string _formDescription = string.Empty;
    public string FormDescription
    {
        get => _formDescription;
        set { _formDescription = value; OnPropertyChanged(); }
    }

    private bool _formIsBuiltIn = false;
    public bool FormIsBuiltIn
    {
        get => _formIsBuiltIn;
        set { _formIsBuiltIn = value; OnPropertyChanged(); }
    }

    private bool _formIsDefault = false;
    public bool FormIsDefault
    {
        get => _formIsDefault;
        set { _formIsDefault = value; OnPropertyChanged(); }
    }
}

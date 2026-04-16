using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace AiToolbox.Demo {
public class ImageGenerationDemo : MonoBehaviour {
    public DalleParameters parameters;
    public UIDocument uiDocument;

    private Button _generateButton;
    private Button _exportButton;
    private TextField _inputField;
    private Label _messageLabel;
    private DropdownField _sizeDropdown;
    private DropdownField _qualityDropdown;

    private Action _cancelCallback;
    private VisualElement _imageContainer;
    private VisualElement _loadingIndicator;

    private void Start() {
        if (string.IsNullOrEmpty(parameters.apiKey)) {
            Debug.LogError("Please assign a DALLE Parameters asset to the Image Generation Demo component.");
            enabled = false;
            return;
        }

        var root = uiDocument.rootVisualElement;

        _imageContainer = root.Q<VisualElement>("generated-image");
        Debug.Assert(_imageContainer != null, "Generated image container not found in UI.");
        _imageContainer.style.display = DisplayStyle.None;

        _loadingIndicator = root.Q<VisualElement>("loading-animation");
        Debug.Assert(_loadingIndicator != null, "Loading animation not found in UI.");
        _loadingIndicator.style.display = DisplayStyle.None;

        // Rotate the loading indicator
        var rotation = 0f;
        var rotationStep = 360f / 60f;
        var rotationCallback = new Action(() => {
            rotation += rotationStep;
            _loadingIndicator.transform.rotation = Quaternion.Euler(0, 0, rotation);
        });
        _loadingIndicator.schedule.Execute(rotationCallback).Every(16);

        _sizeDropdown = root.Q<DropdownField>("size-dropdown");
        Debug.Assert(_sizeDropdown != null, "Size dropdown not found in UI.");
        _sizeDropdown.RegisterCallback<ChangeEvent<string>>(evt => {
            var size = (DalleParameters.Size)Enum.Parse(typeof(DalleParameters.Size), evt.newValue);
            parameters.size = size;
        });
        _sizeDropdown.choices = new List<string>();
        foreach (var size in Enum.GetNames(typeof(DalleParameters.Size))) {
            _sizeDropdown.choices.Add(size);
        }

        _sizeDropdown.index = (int)parameters.size;

        _qualityDropdown = root.Q<DropdownField>("quality-dropdown");
        Debug.Assert(_qualityDropdown != null, "Quality dropdown not found in UI.");
        _qualityDropdown.RegisterCallback<ChangeEvent<string>>(evt => {
            var quality = (DalleParameters.Quality)Enum.Parse(typeof(DalleParameters.Quality), evt.newValue);
            parameters.quality = quality;
        });
        _qualityDropdown.choices = new List<string>();
        foreach (var quality in Enum.GetNames(typeof(DalleParameters.Quality))) {
            _qualityDropdown.choices.Add(quality);
        }

        _qualityDropdown.index = (int)parameters.quality;

        _inputField = root.Q<TextField>("input-text");
        Debug.Assert(_inputField != null, "Input text field not found in UI.");

        _generateButton = root.Q<Button>("generate-button");
        Debug.Assert(_generateButton != null, "Generate button not found in UI.");
        _generateButton.clickable.clicked += OnGenerateClicked;

        _exportButton = root.Q<Button>("export-button");
        Debug.Assert(_exportButton != null, "Export Button not found in UI.");
        _exportButton.clickable.clicked += () => {
#if UNITY_EDITOR
            var basePath = Application.dataPath;
#else
            var basePath = Application.persistentDataPath;
#endif
            var filename = $"dalle_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
            var path = Path.Combine(basePath, filename);
            var texture = _imageContainer.style.backgroundImage.value.texture;
            var bytes = texture.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            _messageLabel.text = $"Image saved to <b>{path}</b>.";
            Debug.Log($"Image saved to {path}.");
#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(path);
#endif
        };

        _messageLabel = root.Q<Label>("message-label");
        Debug.Assert(_messageLabel != null, "Message label not found in UI.");
        _messageLabel.text = $"Type your prompt and press <b>{_generateButton.text}</b> to generate an image.";
    }

    private void OnDestroy() {
        _cancelCallback?.Invoke();
    }

    private void OnGenerateClicked() {
        _imageContainer.style.display = DisplayStyle.None;
        _loadingIndicator.style.display = DisplayStyle.Flex;

        if (_cancelCallback != null) {
            _cancelCallback();
            _cancelCallback = null;
        }

        var text = _inputField.text;
        _cancelCallback = Dalle.Request(text, parameters, texture => {
            _imageContainer.style.display = DisplayStyle.Flex;
            _loadingIndicator.style.display = DisplayStyle.None;
            _messageLabel.style.display = DisplayStyle.None;
            _imageContainer.style.backgroundImage = texture;
        }, (errorCode, errorMessage) => {
            _imageContainer.style.display = DisplayStyle.None;
            _loadingIndicator.style.display = DisplayStyle.None;
            _messageLabel.style.display = DisplayStyle.Flex;
            _messageLabel.text = $"<color=red><b>Error {errorCode}:</b></color> {errorMessage}";
        });
    }

    private void Update() {
        _exportButton?.SetEnabled(_imageContainer.style.backgroundImage.value.texture != null);
    }
}
}
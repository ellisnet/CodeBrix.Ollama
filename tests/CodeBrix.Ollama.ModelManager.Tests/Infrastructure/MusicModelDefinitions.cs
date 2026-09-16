using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// The music-generation models the live tests download, as test-side data: what to pull, what the
/// publisher stated about it on 2026-09-16 (commit, sizes, hashes, licence), and which gate opens it.
/// Generated from ~/ClaudeHome/SPIKE_codebrix_ollama_music_models_2026-09-16.json; the library itself
/// knows nothing about any of these models.
/// </summary>
public static class MusicModelDefinitions
{
    /// <summary>Opens the ~1.1 GB set (also the gate of every other live test in this project).</summary>
    public const string LiveTestsGate = "CODEBRIX_OLLAMA_RUN_LIVE_TESTS";

    /// <summary>Opens the two MuseCoco checkpoints (1.3 GB and 13.9 GB), on top of the live gate.</summary>
    public const string MuseCocoTestsGate = "CODEBRIX_OLLAMA_RUN_MUSECOCO_TESTS";

    /// <summary>Opens the remaining large repositories (about 8 GB), on top of the live gate.</summary>
    public const string LargeMusicTestsGate = "CODEBRIX_OLLAMA_RUN_LARGE_MUSIC_TESTS";

    private const string MagentaBucket = "magentadata";
    private const string MagentaPrefix = "models/music_transformer/";

    /// <summary>
    /// The gate-A SkyTNT filter: the safetensors weights and configuration files only. The ONNX pair,
    /// the duplicate .bin and the training logs are pulled by the large gate instead.
    /// </summary>
    private static readonly FileFilter GateAWeightsOnly = new FileFilter(
        Array.Empty<string>(),
        new[] { "logs/**", "**/*.tfevents*", "optimizer.pt", "**/optimizer*.pt", "onnx/**", "pytorch_model.bin" });

    /// <summary>The safetensors weights and configs only; the ONNX pair, the duplicate .bin and the training logs are left to the large gate.</summary>
    public static readonly MusicModel SkyTntMidiModelTv2oMedium = new MusicModel(
        BundleDefinition.ForHuggingFace(
            "hf.co/skytnt/midi-model-tv2o-medium",
            "skytnt/midi-model-tv2o-medium",
            "0f8f265d4330f4e46527ac2313200254c5757f5f",
            GateAWeightsOnly,
            new LicenseRecord("apache-2.0", "https://huggingface.co/skytnt/midi-model-tv2o-medium", "Licence tag on the Hugging Face model card."),
            "The safetensors weights and configs only; the ONNX pair, the duplicate .bin and the training logs are left to the large gate."),
        LiveTestsGate,
        "0f8f265d4330f4e46527ac2313200254c5757f5f",
        "apache-2.0",
        new[]
        {
            new ExpectedBundleFile(".gitattributes", 1519L, "11ad7efa24975ee4b0c3c3a38ed18737f0658a5f75a0a96787b576a78a023361"),
            new ExpectedBundleFile("README.md", 1279L, "2510e691d4da9fd0647c6c6a226f721a84df5676f56f227abc0ff3b6da471abc"),
            new ExpectedBundleFile("config.json", 2016L, "1f393e1e8c630ddc81a976348ee246549d613f0a117dc0622eed49918dfcb511"),
            new ExpectedBundleFile("generation_config.json", 69L, "e367d0feb45ba71e180b151aaf976961ce48b0a70ba8ed47bf7deba0dae273a3"),
            new ExpectedBundleFile("model.safetensors", 467701064L, "82ac8b2217f8f66f79737e444fe60c686d3cbfee54b0c8ef717f701213bbbb83"),
        });

    /// <summary>The smallest MuPT v1 checkpoint: LlamaForCausalLM weights with the custom ABC-notation tokenizer files.</summary>
    public static readonly MusicModel MuPtV1_190M = new MusicModel(
        BundleDefinition.ForHuggingFace(
            "hf.co/m-a-p/MuPT-v1-8192-190M",
            "m-a-p/MuPT-v1-8192-190M",
            "bf8f270d11683f65aa83ccebc2e756b782b795d8",
            FileFilter.ExcludeTrainingArtifacts,
            new LicenseRecord("apache-2.0", "https://huggingface.co/m-a-p/MuPT-v1-8192-190M", "Licence tag on the Hugging Face model card."),
            "The smallest MuPT v1 checkpoint: LlamaForCausalLM weights with the custom ABC-notation tokenizer files."),
        LiveTestsGate,
        "bf8f270d11683f65aa83ccebc2e756b782b795d8",
        "apache-2.0",
        new[]
        {
            new ExpectedBundleFile(".gitattributes", 1519L, "11ad7efa24975ee4b0c3c3a38ed18737f0658a5f75a0a96787b576a78a023361"),
            new ExpectedBundleFile("README.md", 10161L, "3e7b3241c82e045a87fcc6c6f182703af68409f7c82b459b7c3d4c12fe5eb563"),
            new ExpectedBundleFile("Yi_logo.svg", 865L, "5090918c50607b9dd60cd5b65dc8cfad785763198143f8207c1660214c909385"),
            new ExpectedBundleFile("config.json", 695L, "ef2f8d080ca8d8431b665cd641d5f46c7e670cb98f0f9f707b04ece116d92f2e"),
            new ExpectedBundleFile("generation_config.json", 132L, "8941d5ad16b22631f0dc77d1b61eb0f345ea947feee6d24fcb7e8246f109311f"),
            new ExpectedBundleFile("m-a-p.png", 5248L, "d813d1f2d17c1513db71163f0e32a0e9c1b93f6ad3a086ee81ed93410b2cf2a2"),
            new ExpectedBundleFile("merges.txt", 434549L, "8500fb019017fb5ea0f0b92cf313ba51c60e35bab04f7a4216128287b9a3045a"),
            new ExpectedBundleFile("pytorch_model.bin", 380166726L, "2ecd397eac330d16e9ae27ef2bbb0bb8bbb7c7e23b152c9a52477d23fea098e9"),
            new ExpectedBundleFile("tokenization_mupt.py", 14982L, "463c2ef33535476ab1dbce525c9b69e611425f1c33b36007c35245d956da44d3"),
            new ExpectedBundleFile("tokenizer_config.json", 190L, "0dda860a77ab84cc65450ac5efe57be09e86e76701c4502ec1c0681082ef3609"),
            new ExpectedBundleFile("vocab.json", 1118607L, "9ed141df45fddaa1ab5bc194e1e06027737fe1614e0c69fef49226473d4f10f8"),
        });

    /// <summary>The TensorFlow 1 checkpoint triple of the unconditional Music Transformer, from the public magentadata bucket; md5 stated by the bucket, sha256 computed.</summary>
    public static readonly MusicModel MagentaMusicTransformerUnconditional = new MusicModel(
        BundleDefinition.ForFileList(
            "storage.googleapis.com/magentadata/music-transformer:unconditional-16",
            new[]
            {
                new BundleFile("checkpoints/unconditional_model_16.ckpt.data-00000-of-00001", "https://storage.googleapis.com/magentadata/models/music_transformer/checkpoints/unconditional_model_16.ckpt.data-00000-of-00001", 203152624L, "d2b51fd9756a004469ef755274580d7edeac0cf7b015a1fe7c9c260fd3ac4dc6", "af63fbf63ae26fe6d66140ee7c22a515", null),
                new BundleFile("checkpoints/unconditional_model_16.ckpt.index", "https://storage.googleapis.com/magentadata/models/music_transformer/checkpoints/unconditional_model_16.ckpt.index", 7604L, "512b9fec519de8e55e25256408201b3939e9d498008c54af5cb2bc2fef03bd29", "11b539fe598891a20fb0ac6c55a036af", null),
                new BundleFile("checkpoints/unconditional_model_16.ckpt.meta", "https://storage.googleapis.com/magentadata/models/music_transformer/checkpoints/unconditional_model_16.ckpt.meta", 79691958L, "b8fdff38b799d1b4800747b4f7e076ba4a97040087e75d0a7130730f838d9aa0", "c7c7af98d23db6236a9d7fbc5474a40d", null),
            },
            FileFilter.Default,
            LicenseRecord.None,
            "The TensorFlow 1 checkpoint triple of the unconditional Music Transformer, from the public magentadata bucket; md5 stated by the bucket, sha256 computed."),
        LiveTestsGate,
        null,
        null,
        new[]
        {
            new ExpectedBundleFile("checkpoints/unconditional_model_16.ckpt.data-00000-of-00001", 203152624L, "d2b51fd9756a004469ef755274580d7edeac0cf7b015a1fe7c9c260fd3ac4dc6"),
            new ExpectedBundleFile("checkpoints/unconditional_model_16.ckpt.index", 7604L, "512b9fec519de8e55e25256408201b3939e9d498008c54af5cb2bc2fef03bd29"),
            new ExpectedBundleFile("checkpoints/unconditional_model_16.ckpt.meta", 79691958L, "b8fdff38b799d1b4800747b4f7e076ba4a97040087e75d0a7130730f838d9aa0"),
        });

    /// <summary>The first half of the MuseCoco pipeline (text to musical attributes); optimizer.pt is training state and is excluded.</summary>
    public static readonly MusicModel MuseCocoText2Attribute = new MusicModel(
        BundleDefinition.ForHuggingFace(
            "hf.co/XinXuNLPer/MuseCoco_text2attribute",
            "XinXuNLPer/MuseCoco_text2attribute",
            "3a4f5610206240f6d81971a0e69f7489dcc8ba2e",
            FileFilter.ExcludeTrainingArtifacts,
            new LicenseRecord("apache-2.0", "https://huggingface.co/XinXuNLPer/MuseCoco_text2attribute", "Licence tag on the Hugging Face model card."),
            "The first half of the MuseCoco pipeline (text to musical attributes); optimizer.pt is training state and is excluded."),
        MuseCocoTestsGate,
        "3a4f5610206240f6d81971a0e69f7489dcc8ba2e",
        "apache-2.0",
        new[]
        {
            new ExpectedBundleFile(".gitattributes", 1519L, "11ad7efa24975ee4b0c3c3a38ed18737f0658a5f75a0a96787b576a78a023361"),
            new ExpectedBundleFile("README.md", 1329L, "6d5366154f09e750668eac7d445b7c362fc63f94f22e211386c75bc2b101beb8"),
            new ExpectedBundleFile("config.json", 671L, "a016f90383251b42e6fb3b398c50272787a60e3a59a233cb0fea797d17a46d2f"),
            new ExpectedBundleFile("pytorch_model.bin", 1345836413L, "85dd23b1a3b6ea8927b77cdb4a011d8396c4f2f23d8808938369dbe58e1ea71b"),
            new ExpectedBundleFile("rng_state.pth", 14503L, "06112c5dd2b14b603eb4d319c9a8976522fc06d0d57b0aa21e5d7951d8f502b4"),
            new ExpectedBundleFile("scheduler.pt", 623L, "3d0299e7e1e603da293528498e755272e6eb836681af020904a37607cab21bfa"),
            new ExpectedBundleFile("special_tokens_map.json", 1232L, "923798606d657978c103dcda3dc047e33bbf6a571058475183e58ea4abe80495"),
            new ExpectedBundleFile("tokenizer.json", 722622L, "4dff9640b5ca42d043853ce894d464ef9225644a9a70ac77c13ff3e344dc6b24"),
            new ExpectedBundleFile("tokenizer_config.json", 349L, "80c107818a1f64b29fc85e2d26e375ed7e10986a46b9cab5b77a459ebfeac299"),
            new ExpectedBundleFile("trainer_state.json", 78568L, "8bf2d60db86f92df122e81179b115399638928e9e1a1c1e0d5ac7ece73ebb2d5"),
            new ExpectedBundleFile("training_args.bin", 3439L, "23a98370963377b887bc86f94f27dd21510a1c697e1c0334a6bacd6b7818e450"),
            new ExpectedBundleFile("vocab.txt", 231508L, "07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3"),
        });

    /// <summary>The second half of the MuseCoco pipeline (attributes to music); a single 13.9 GB checkpoint.</summary>
    public static readonly MusicModel MuseCocoAttribute2Music = new MusicModel(
        BundleDefinition.ForHuggingFace(
            "hf.co/XinXuNLPer/MuseCoco_attribute2music",
            "XinXuNLPer/MuseCoco_attribute2music",
            "8d9df584e3e09da88601546e7980323221f12644",
            FileFilter.ExcludeTrainingArtifacts,
            new LicenseRecord("apache-2.0", "https://huggingface.co/XinXuNLPer/MuseCoco_attribute2music", "Licence tag on the Hugging Face model card."),
            "The second half of the MuseCoco pipeline (attributes to music); a single 13.9 GB checkpoint."),
        MuseCocoTestsGate,
        "8d9df584e3e09da88601546e7980323221f12644",
        "apache-2.0",
        new[]
        {
            new ExpectedBundleFile(".gitattributes", 1519L, "11ad7efa24975ee4b0c3c3a38ed18737f0658a5f75a0a96787b576a78a023361"),
            new ExpectedBundleFile("README.md", 390L, "ec79a509f9bb77eac833857ef638e54214a73f1a461c76e1ab1df0ebd82afdc6"),
            new ExpectedBundleFile("attribute2music.pt", 14546294489L, "b3d99658eee895f8773a86d1f41e32e9e11df7e0c4b0e899e175e31b9f62955f"),
        });

    /// <summary>The original SkyTNT model with every published format: safetensors, .bin, the ONNX pair and the soundfont.</summary>
    public static readonly MusicModel SkyTntMidiModel = new MusicModel(
        BundleDefinition.ForHuggingFace(
            "hf.co/skytnt/midi-model",
            "skytnt/midi-model",
            "94f8dc5af7e744da32913dea85d720c7be148091",
            FileFilter.ExcludeTrainingArtifacts,
            new LicenseRecord("apache-2.0", "https://huggingface.co/skytnt/midi-model", "Licence tag on the Hugging Face model card."),
            "The original SkyTNT model with every published format: safetensors, .bin, the ONNX pair and the soundfont."),
        LargeMusicTestsGate,
        "94f8dc5af7e744da32913dea85d720c7be148091",
        "apache-2.0",
        new[]
        {
            new ExpectedBundleFile(".gitattributes", 1569L, "314d4ba425b37a20680d5208c7cf8fe673d303c44bd341a2fb1f6a03f42c1ed2"),
            new ExpectedBundleFile("README.md", 1406L, "b334c410a45a9954ab171fd44926e0d6d216973ff49e4319752f550a677ae488"),
            new ExpectedBundleFile("config.json", 1729L, "4c8f615415315405ab11eb5b28ed4232e035d123de4377eef3a488b55f04857c"),
            new ExpectedBundleFile("generation_config.json", 69L, "e367d0feb45ba71e180b151aaf976961ce48b0a70ba8ed47bf7deba0dae273a3"),
            new ExpectedBundleFile("model.safetensors", 933334232L, "61b7c01753df96e30b9e6a9c89300f7d69d1da82d4fe6a8d3740f4c7884b0ce5"),
            new ExpectedBundleFile("onnx/model_base.onnx", 821029854L, "66c80e17d437c7c8f00d5974849f55fc3aa6ae55db4def3f2ba18670b7f7d8a9"),
            new ExpectedBundleFile("onnx/model_token.onnx", 115293316L, "7966c5a853424797de4e3ea3d3eb65f0544b18d8b5a11d00cbb1b45d99968416"),
            new ExpectedBundleFile("pytorch_model.bin", 933367626L, "b294cd18e89de546cca434d33f6bbda61c9d9108e04e6b58a8575654d55400ff"),
            new ExpectedBundleFile("soundfont.sf2", 51278610L, "5ea2375e8bd7d8e71def1036978c1621e85b66934169b6a2744b27b9b3c2d99c"),
        });

    /// <summary>The tv2o-medium repository with the ONNX pair and the duplicate .bin included (logs excluded); stored under its own tag so it never collides with the gate-A pull.</summary>
    public static readonly MusicModel SkyTntMidiModelTv2oMediumFull = new MusicModel(
        BundleDefinition.ForHuggingFace(
            "hf.co/skytnt/midi-model-tv2o-medium:full",
            "skytnt/midi-model-tv2o-medium",
            "0f8f265d4330f4e46527ac2313200254c5757f5f",
            FileFilter.ExcludeTrainingArtifacts,
            new LicenseRecord("apache-2.0", "https://huggingface.co/skytnt/midi-model-tv2o-medium", "Licence tag on the Hugging Face model card."),
            "The tv2o-medium repository with the ONNX pair and the duplicate .bin included (logs excluded); stored under its own tag so it never collides with the gate-A pull."),
        LargeMusicTestsGate,
        "0f8f265d4330f4e46527ac2313200254c5757f5f",
        "apache-2.0",
        new[]
        {
            new ExpectedBundleFile(".gitattributes", 1519L, "11ad7efa24975ee4b0c3c3a38ed18737f0658a5f75a0a96787b576a78a023361"),
            new ExpectedBundleFile("README.md", 1279L, "2510e691d4da9fd0647c6c6a226f721a84df5676f56f227abc0ff3b6da471abc"),
            new ExpectedBundleFile("config.json", 2016L, "1f393e1e8c630ddc81a976348ee246549d613f0a117dc0622eed49918dfcb511"),
            new ExpectedBundleFile("generation_config.json", 69L, "e367d0feb45ba71e180b151aaf976961ce48b0a70ba8ed47bf7deba0dae273a3"),
            new ExpectedBundleFile("model.safetensors", 467701064L, "82ac8b2217f8f66f79737e444fe60c686d3cbfee54b0c8ef717f701213bbbb83"),
            new ExpectedBundleFile("onnx/model_base.onnx", 821713887L, "1af68e16a0509936caeb70d322ab30619d784c7af72df019f3a59276c67c1bc4"),
            new ExpectedBundleFile("onnx/model_token.onnx", 116661381L, "cce9376abe8f5c8d51d1b110580cc8501d38679af55ebd5a332e757e62ab9911"),
            new ExpectedBundleFile("pytorch_model.bin", 467731642L, "88fb164517d86c3c1433a1ab315b25c0d271443b81d73a101bee3c46f09779e8"),
        });

    /// <summary>A LoRA adapter over tv2o-medium; the repository also carries its own copies of the ONNX pair.</summary>
    public static readonly MusicModel SkyTntMidiModelTv2omJpopLora = new MusicModel(
        BundleDefinition.ForHuggingFace(
            "hf.co/skytnt/midi-model-tv2om-jpop-lora",
            "skytnt/midi-model-tv2om-jpop-lora",
            "c1559e14b2af03c59c49be31e8da1a9f6d428fb9",
            FileFilter.ExcludeTrainingArtifacts,
            new LicenseRecord("apache-2.0", "https://huggingface.co/skytnt/midi-model-tv2om-jpop-lora", "Licence tag on the Hugging Face model card."),
            "A LoRA adapter over tv2o-medium; the repository also carries its own copies of the ONNX pair."),
        LargeMusicTestsGate,
        "c1559e14b2af03c59c49be31e8da1a9f6d428fb9",
        "apache-2.0",
        new[]
        {
            new ExpectedBundleFile(".gitattributes", 1519L, "11ad7efa24975ee4b0c3c3a38ed18737f0658a5f75a0a96787b576a78a023361"),
            new ExpectedBundleFile("README.md", 104L, "0821f6787942184476022ab4fa7601102fc5cfc3f2e51300680269739845f043"),
            new ExpectedBundleFile("adapter_config.json", 731L, "3a6482db2368c89835421ede805bdf94d08cc032409821b29c93d14385ab0a5b"),
            new ExpectedBundleFile("adapter_model.safetensors", 41705144L, "9b94ee0a41057a28733e1e67144c3f8cceb3dcf3ec540ee7e4678090858115ed"),
            new ExpectedBundleFile("onnx/model_base.onnx", 821713887L, "2ade62b1facd918030936e5d5132dbba07a50d2912985fcc165275c7d6528164"),
            new ExpectedBundleFile("onnx/model_token.onnx", 116661381L, "7ad02a07210fd2081a5d41c872fbb5f924fb6c6a65c5ad6bfcf299cc0ba06aa6"),
        });

    /// <summary>A second LoRA adapter over tv2o-medium.</summary>
    public static readonly MusicModel SkyTntMidiModelTv2omTouhouLora = new MusicModel(
        BundleDefinition.ForHuggingFace(
            "hf.co/skytnt/midi-model-tv2om-touhou-lora",
            "skytnt/midi-model-tv2om-touhou-lora",
            "13bc5efd8b0049a094ad3a0faed7d78a86d73c33",
            FileFilter.ExcludeTrainingArtifacts,
            new LicenseRecord("apache-2.0", "https://huggingface.co/skytnt/midi-model-tv2om-touhou-lora", "Licence tag on the Hugging Face model card."),
            "A second LoRA adapter over tv2o-medium."),
        LargeMusicTestsGate,
        "13bc5efd8b0049a094ad3a0faed7d78a86d73c33",
        "apache-2.0",
        new[]
        {
            new ExpectedBundleFile(".gitattributes", 1519L, "11ad7efa24975ee4b0c3c3a38ed18737f0658a5f75a0a96787b576a78a023361"),
            new ExpectedBundleFile("README.md", 103L, "354384fa7c075fbfcac18c8e009394c5928c984360873af9af524b3ba2173720"),
            new ExpectedBundleFile("adapter_config.json", 731L, "9b71a0effa9b9606db1fa031ddd91ca398b7be01b8dd57b9f2efb14eb87ace12"),
            new ExpectedBundleFile("adapter_model.safetensors", 41705144L, "7bbbccb1ff2e1cc7db88d122cea5728e50a47568df4a8ef096fd22908d2c0dae"),
            new ExpectedBundleFile("onnx/model_base.onnx", 821713887L, "d54f185fca962934dcd547403654ea134261d27b4292c915c4e67091bac28d27"),
            new ExpectedBundleFile("onnx/model_token.onnx", 116661381L, "7aae1f7d6514aa52523a097ae078edded341dd7492f225d7fd86f19f057c5ff9"),
        });

    /// <summary>The largest MuPT v1 checkpoint.</summary>
    public static readonly MusicModel MuPtV1_1_97B = new MusicModel(
        BundleDefinition.ForHuggingFace(
            "hf.co/m-a-p/MuPT-v1-8192-1.97B",
            "m-a-p/MuPT-v1-8192-1.97B",
            "a5854dea880bedfe27b07896eaf2906d043bc505",
            FileFilter.ExcludeTrainingArtifacts,
            new LicenseRecord("apache-2.0", "https://huggingface.co/m-a-p/MuPT-v1-8192-1.97B", "Licence tag on the Hugging Face model card."),
            "The largest MuPT v1 checkpoint."),
        LargeMusicTestsGate,
        "a5854dea880bedfe27b07896eaf2906d043bc505",
        "apache-2.0",
        new[]
        {
            new ExpectedBundleFile(".gitattributes", 1519L, "11ad7efa24975ee4b0c3c3a38ed18737f0658a5f75a0a96787b576a78a023361"),
            new ExpectedBundleFile("README.md", 10160L, "2b0b50f8d32693807379d7ed5ee065245b93b9afab1dc2e3b2b29577cde3040f"),
            new ExpectedBundleFile("Yi_logo.svg", 865L, "5090918c50607b9dd60cd5b65dc8cfad785763198143f8207c1660214c909385"),
            new ExpectedBundleFile("config.json", 696L, "81344c22fbcc96bc42cc2d3f40e55e69fcea4ab04e7b147213e2641c14606faa"),
            new ExpectedBundleFile("generation_config.json", 132L, "8941d5ad16b22631f0dc77d1b61eb0f345ea947feee6d24fcb7e8246f109311f"),
            new ExpectedBundleFile("m-a-p.png", 5248L, "d813d1f2d17c1513db71163f0e32a0e9c1b93f6ad3a086ee81ed93410b2cf2a2"),
            new ExpectedBundleFile("merges.txt", 434549L, "8500fb019017fb5ea0f0b92cf313ba51c60e35bab04f7a4216128287b9a3045a"),
            new ExpectedBundleFile("pytorch_model.bin", 3931517782L, "5c92f4135b558bede523143bb2ff000f717cc1a2c6c30f39a8cdd1370d10ed35"),
            new ExpectedBundleFile("tokenization_mupt.py", 14982L, "463c2ef33535476ab1dbce525c9b69e611425f1c33b36007c35245d956da44d3"),
            new ExpectedBundleFile("tokenizer_config.json", 190L, "0dda860a77ab84cc65450ac5efe57be09e86e76701c4502ec1c0681082ef3609"),
            new ExpectedBundleFile("vocab.json", 1118607L, "9ed141df45fddaa1ab5bc194e1e06027737fe1614e0c69fef49226473d4f10f8"),
        });

    /// <summary>The melody-conditioned Music Transformer checkpoint triple.</summary>
    public static readonly MusicModel MagentaMusicTransformerMelodyConditioned = new MusicModel(
        BundleDefinition.ForFileList(
            "storage.googleapis.com/magentadata/music-transformer:melody-conditioned-16",
            new[]
            {
                new BundleFile("checkpoints/melody_conditioned_model_16.ckpt.data-00000-of-00001", "https://storage.googleapis.com/magentadata/models/music_transformer/checkpoints/melody_conditioned_model_16.ckpt.data-00000-of-00001", 474065848L, "99681b44ca42a41b7e964c45772de925f6c2001454655450169f47cdfeec829b", "1987b3c0922efa89d643f1d345af8bfe", null),
                new BundleFile("checkpoints/melody_conditioned_model_16.ckpt.index", "https://storage.googleapis.com/magentadata/models/music_transformer/checkpoints/melody_conditioned_model_16.ckpt.index", 18427L, "21a80015a495c9f249c42146110c3155f521f17deab61db8a5c9bf9781b78610", "0e7e3139aa7fbf75851358c27988ab95", null),
                new BundleFile("checkpoints/melody_conditioned_model_16.ckpt.meta", "https://storage.googleapis.com/magentadata/models/music_transformer/checkpoints/melody_conditioned_model_16.ckpt.meta", 97593939L, "41d0f5e33cd5e070218024e8416e417d25b7cf3d0a1d61ac0c57f5f4efc6acf7", "575915aa40890ee4fb5da3f1f6b2efa8", null),
            },
            FileFilter.Default,
            LicenseRecord.None,
            "The melody-conditioned Music Transformer checkpoint triple."),
        LargeMusicTestsGate,
        null,
        null,
        new[]
        {
            new ExpectedBundleFile("checkpoints/melody_conditioned_model_16.ckpt.data-00000-of-00001", 474065848L, "99681b44ca42a41b7e964c45772de925f6c2001454655450169f47cdfeec829b"),
            new ExpectedBundleFile("checkpoints/melody_conditioned_model_16.ckpt.index", 18427L, "21a80015a495c9f249c42146110c3155f521f17deab61db8a5c9bf9781b78610"),
            new ExpectedBundleFile("checkpoints/melody_conditioned_model_16.ckpt.meta", 97593939L, "41d0f5e33cd5e070218024e8416e417d25b7cf3d0a1d61ac0c57f5f4efc6acf7"),
        });

    /// <summary>The six primer MIDI files the Music Transformer notebook used.</summary>
    public static readonly MusicModel MagentaMusicTransformerPrimers = new MusicModel(
        BundleDefinition.ForFileList(
            "storage.googleapis.com/magentadata/music-transformer:primers",
            new[]
            {
                new BundleFile("primers/c_major_arpeggio.mid", "https://storage.googleapis.com/magentadata/models/music_transformer/primers/c_major_arpeggio.mid", 105L, "56de149a9eacecbf693ea8047ca6c6a096f3c3a03d6480e83caef4f7c9d4be3a", "933fbdfdfcefadcaa7dd677f6f64167e", null),
                new BundleFile("primers/c_major_scale.mid", "https://storage.googleapis.com/magentadata/models/music_transformer/primers/c_major_scale.mid", 107L, "107b711aefd8d3db5fd2bc6c4c37593795b6fa4918417b3e41b2b03da8ffae29", "8e260d228353ff23fc59bb07b7504c2b", null),
                new BundleFile("primers/clair_de_lune.mid", "https://storage.googleapis.com/magentadata/models/music_transformer/primers/clair_de_lune.mid", 146L, "6a18e2574b8c212e3d8c952a7cee9ee07722c467bb4fdd63a7f6f06bac28f210", "9db4689c38fda0c2624e77ef04166930", null),
                new BundleFile("primers/fur_elise.mid", "https://storage.googleapis.com/magentadata/models/music_transformer/primers/fur_elise.mid", 131L, "f959c5b86d982c939cd54b4d84278ba89168bd99db05d8200517a3a07de851ae", "afc0010e22b1c70760696cac123bbf29", null),
                new BundleFile("primers/moonlight_sonata.mid", "https://storage.googleapis.com/magentadata/models/music_transformer/primers/moonlight_sonata.mid", 147L, "48405712509d29a1754f3c399e962fb5f49db31267f54cbda8e4ed43ce08fa0d", "2c94bf5d1e4432b8425979123eefd804", null),
                new BundleFile("primers/prelude_in_c_major.mid", "https://storage.googleapis.com/magentadata/models/music_transformer/primers/prelude_in_c_major.mid", 153L, "afb9b23e28a239c784d2fc5c5327c7ea83737fdceb1460311a8799a5925d3789", "7700ec085698b0de500fe3c37ba12d7d", null),
            },
            FileFilter.Default,
            LicenseRecord.None,
            "The six primer MIDI files the Music Transformer notebook used."),
        LargeMusicTestsGate,
        null,
        null,
        new[]
        {
            new ExpectedBundleFile("primers/c_major_arpeggio.mid", 105L, "56de149a9eacecbf693ea8047ca6c6a096f3c3a03d6480e83caef4f7c9d4be3a"),
            new ExpectedBundleFile("primers/c_major_scale.mid", 107L, "107b711aefd8d3db5fd2bc6c4c37593795b6fa4918417b3e41b2b03da8ffae29"),
            new ExpectedBundleFile("primers/clair_de_lune.mid", 146L, "6a18e2574b8c212e3d8c952a7cee9ee07722c467bb4fdd63a7f6f06bac28f210"),
            new ExpectedBundleFile("primers/fur_elise.mid", 131L, "f959c5b86d982c939cd54b4d84278ba89168bd99db05d8200517a3a07de851ae"),
            new ExpectedBundleFile("primers/moonlight_sonata.mid", 147L, "48405712509d29a1754f3c399e962fb5f49db31267f54cbda8e4ed43ce08fa0d"),
            new ExpectedBundleFile("primers/prelude_in_c_major.mid", 153L, "afb9b23e28a239c784d2fc5c5327c7ea83737fdceb1460311a8799a5925d3789"),
        });

    /// <summary>Every definition, in gate order.</summary>
    public static IReadOnlyList<MusicModel> All { get; } = new[]
    {
        SkyTntMidiModelTv2oMedium, MuPtV1_190M, MagentaMusicTransformerUnconditional,
        MuseCocoText2Attribute, MuseCocoAttribute2Music,
        SkyTntMidiModel, SkyTntMidiModelTv2oMediumFull, SkyTntMidiModelTv2omJpopLora, SkyTntMidiModelTv2omTouhouLora,
        MuPtV1_1_97B, MagentaMusicTransformerMelodyConditioned, MagentaMusicTransformerPrimers,
    };
}

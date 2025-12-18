# VLAM Assembly Reference Mapper

VLAM Assembly Reference Mapper is a Unity Editor tool that scans assemblies and builds a **global namespace → assembly ownership index**.

The result is a shared lookup file (`AssemblyNamespaceIndex.json`) that makes it possible to reliably resolve which assembly *owns* a given namespace — even across packages, SDKs, and custom projects.

This tool is designed to be **collaborative**: contributors can submit pull requests with their generated index to continuously expand and improve the global namespace database.

---

## What Problem This Solves

Unity projects — especially VRChat, SDK-heavy, or modular setups — often suffer from:

* Ambiguous namespaces shared across multiple assemblies
* Hard-to-debug compile errors caused by missing or incorrect assembly references
* No authoritative source of truth for namespace ownership

VLAM Assembly Reference Mapper creates that source of truth.

---

## How It Works

1. You select folders that contain `.asmdef` files (Assets and/or Packages)
2. The tool scans each assembly and all C# files under it
3. All declared namespaces are extracted and indexed
4. Each namespace is mapped to the assembly that defines it
5. The result is saved as `AssemblyNamespaceIndex.json`

The scanner is incremental and non-blocking:

* Unity remains responsive
* A progress bar shows current progress and scanned files
* Scans can be canceled safely

---

## Installation

1. Clone or download this repository
2. Place the tool anywhere inside your Unity project (e.g. `Assets/VirtuaLabs/Editor`)
3. Open Unity
4. Navigate to:

```
Tools → VirtuaLabs → Assembly Reference Mapper
```

---

## Usage

### 1. Add Folders

You can add folders in multiple ways:

* Drag & drop folders into the window
* Click **Add Folder…** to select a folder manually
* Click **Add All Packages** to scan all non-Unity packages

Only folders containing `.asmdef` files are relevant.

---

### 2. Scan

Click **Scan & Build Namespace Index**.

During the scan you will see:

* Current assembly being processed
* Current C# file being scanned
* Overall progress

Unity remains usable while the scan runs.

---

### 3. Review Results

After completion, the **Namespace Index** section shows:

* Total number of discovered namespaces
* The assembly that owns each namespace
* Whether the source is `Assets` or `Packages`

You can:

* Filter by namespace or assembly name
* Expand / collapse the list

---

### 4. Export or Clear

* **Export**: Save the index to any location
* **Clear**: Remove all learned mappings and start fresh

---

## Contributing to the Global Index

This project thrives on community contributions.

### How to Contribute

1. Run the tool in your project
2. Review the generated `AssemblyNamespaceIndex.json`
3. Make sure paths are correct and normalized (`/`, not `\\`)
4. Submit a **pull request** containing your updated JSON file

Each contribution helps expand the global namespace knowledge base and improves accuracy for everyone.

---

## Best Practices

* Avoid scanning temporary or generated folders
* Prefer Packages over assets when conflicts exist
* Keep commits focused on namespace data only

---

## Output File

The index is stored at:

```
Assets/VirtuaLabs/Resources/AssemblyNamespaceIndex.json
```

Structure overview:

```json
{
  "Namespace.Name": {
    "assemblyName": "Assembly.Name",
    "asmdefPath": "Packages/example/path.asmdef",
    "source": "Packages"
  }
}
```

---

## License

This project is licensed under the **VirtuaLabs Non-Commercial Attribution License**.

✔ Free for non-commercial use
✔ Attribution required

✘ Commercial use is not permitted
✘ Selling this tool or anything made using it is not allowed

Commercial licenses are available — contact VirtuaLabs for details.

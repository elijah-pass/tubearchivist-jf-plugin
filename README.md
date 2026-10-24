<h1 align="center">Jellyfin TubeArchivist Plugin</h1>

<p align="center">
<img alt="Plugin Banner" src="https://raw.githubusercontent.com/elijah-pass/tubearchivist-jf-plugin/master/images/logo.png"/>
<br/>
</p>

> [!IMPORTANT]
> This repository publishes an unofficial downstream build of TubeArchivistMetadata containing locally maintained fixes. It is not affiliated with or supported by the upstream TubeArchivist project. The original project is available at [tubearchivist/tubearchivist-jf-plugin](https://github.com/tubearchivist/tubearchivist-jf-plugin).

> [!IMPORTANT]
> Version `1.5.0.1` targets Jellyfin `12.0` and requires .NET `10`. For Jellyfin `10.11`, use downstream version `1.4.4.2` instead.

## About

<p>This plugin adds the metadata provider for <a href="https://www.tubearchivist.com/">TubeArchivist</a>, offering improved flexibility and native integration with Jellyfin compared to previous solutions.</p>

## How it works
The media organization is a `Shows` collection, where each channel is a show and its videos are the episodes, organized in seasons by year.<br>
The plugin interacts with TubeArchivist APIs to fetch videos and channels metadata.

### Features
- Add metadata for videos (episodes)
- Add metadata for channels (shows)
- Add images for videos (episodes), ie. thumb images
- Add images for channels (shows), ie. thumb, tvart and banner images
- Organize videos (episodes) by year (seasons)
- Bidirectional playback progress synchronization
- Bidirectional playlists synchronization

> [!WARNING]
> Enabling synchronization in both directions you can run in race conditions and unexpected results.

## Installation
### From this unofficial downstream repository
1. Go to `Dashboard -> Plugins` and click on the `Manage Repositories` button
2. Add a new repository with the following details:
- Repository name: `TubeArchivistMetadata (elijah-pass fork)`
- Repository URL: `https://raw.githubusercontent.com/elijah-pass/tubearchivist-jf-plugin/master/manifest.json`
  ![Add repository](https://github.com/user-attachments/assets/337ba921-bc97-47ea-815c-c664cf7661a2)

> [!CAUTION]
> Do not enable this repository and the upstream TubeArchivist plugin repository at the same time. Both catalogs contain the same plugin GUID because this build is intended as a drop-in replacement that preserves existing configuration.

3. Go back to the catalog
4. Find `TubeArchivistMetadata` and install it
![Find plugin](https://github.com/user-attachments/assets/41f7315b-27c6-47dd-958f-21a232c30013)
5. Restart Jellyfin to apply the changes

### From ZIP in GitHub releases
1. Download a compatible release (`tubearchivistmetadata_*.zip`) from this fork's [releases](https://github.com/elijah-pass/tubearchivist-jf-plugin/releases).
2. Stop Jellyfin before changing plugin files.
3. Extract the complete package into a single `plugins/TubeArchivistMetadata` folder (create it if needed).
4. Start Jellyfin to apply the changes. See the upgrade guidance below if another TubeArchivist plugin build is already installed.

### Upgrade and recover missing metadata

This fork retains the upstream plugin GUID and configuration identity so existing settings can be reused. Before upgrading, back up Jellyfin's configuration, including plugin settings, and check the Jellyfin version required by the release.

1. Disable the upstream TubeArchivist repository and keep this fork's repository enabled, then install the compatible release. If installing manually, stop Jellyfin first, move older TubeArchivist plugin folders outside the `plugins` directory, and install the complete new package in one folder. Older folders may contain spaces or version suffixes; there must be only one active copy of `Jellyfin.Plugin.TubeArchivistMetadata.dll`. Keep the existing plugin configuration XML.
2. Restart Jellyfin and confirm that the dashboard lists one `TubeArchivistMetadata` plugin with the expected version. Check the collection display name, TubeArchivist address, API key, and synchronization settings.
3. In the TubeArchivist library settings, enable the `TubeArchivist` metadata and image providers for shows and episodes. The collection display name in plugin settings must match this library's name.
4. For a channel with missing titles, descriptions, thumbnails, or year seasons, run **Refresh metadata** on the channel and choose **Replace all metadata**. Run the same refresh on the whole TubeArchivist library to repair all affected channels. Select **Replace existing images** if thumbnails need refreshing too, and wait for the refresh to finish.
5. Verify that videos have titles and are grouped under their publication-year seasons. Refresh the channel or library, rather than only an individual video, when repairing season grouping.

The metadata refresh repairs Jellyfin's library records; leave the TubeArchivist media directories in their existing layout. If the plugin fails to load, stop Jellyfin and restore the previous compatible plugin package and backed-up settings.

## Configuration
<p>This plugin requires that you have already an instance of TubeArchivist up and running.</p>
Once installed, you have to configure the following parameters in the plugin configuration:
<ul>
    <li>Collection display name</li>
    <li>TubeArchivist instance address</li>
    <li>TubeArchivist API key</li>
    <li>Overviews length (channels and videos descriptions)</li>
    <li>Playback synchronization settings discussed in the <a href="#playback-synchronization">Playback synchronization</a> paragraph</li>
</ul>

![Plugin configuration](https://github.com/tubearchivist/tubearchivist-jf-plugin/assets/31162436/d34464ea-ddfb-44b3-9d3e-5d5974956c58)


## Use the plugin
<p>Using the plugin is very simple. Let's start from the beginning:</p>

_NOTE: If you are using Docker containers, it is important to mount the TubeArchivist media path into Jellyfin container as **read-only**, in order to avoid possible operations on the media files that will break TubeArchivist._ <br>
1. Go to `Dashboard -> Libraries` and add a media library
2. In the form select `Shows` as Content type, set a display name for the library and set the TubeArchivist media folder in the `Folders` section
![Add library](https://github.com/tubearchivist/tubearchivist-jf-plugin/assets/31162436/1eca534e-0929-4134-8587-3cff0009f618)

4. Scrolling down, uncheck all metadata and image providers except `TubeArchivist`. (You won't find TubeArchivist in seasons providers, so just disable everything there)
5. Save and come back to Home, you will see the newly added library. Jellyfin will have executed the metadata fetching for you after the collection creation and then you will see the metadata and the images of channels and videos


## Synchronization
This plugin has different bidirectional sycnhronization features, that can be configured in the specific section in the plugin configuration page:
![Synchronization settings](https://github.com/user-attachments/assets/b0bb556b-fce3-4a3e-bc6c-b0a2b482cedc)

### Playback synchronization
<p>Starting from v1.3.1 this plugin offers playback progress and watched status bidirectional synchronization, but you can choose to enable only a one way synchronization (Jellyfin -> TubeArchivist or TubeArchivist -> Jellyfin) too.</p>

#### Jellyfin -> TubeArchivist playback synchronization
<p>This kind of synchronization is done listening for progress and watched status changes while playing the videos for the specified users.<br>Furthermore, there is a task that runs at Jellyfin startup to synchronize the whole library.</p>
<p>In the text field you can specify one Jellyfin username to synchronize data of to TubeArchivist.</p>

#### TubeArchivist -> Jellyfin playback synchronization
<p>This kind of synchronization is done using a Jellyfin scheduled task that regularly synchronizes data from TubeArchivist API to Jellyfin.</p>
<p>In the text field you can specify one or more Jellyfin usernames to update data for.</p>

### Playlists synchronization
<p>Starting from v.1.4.1 this plugin offers playlists bidirectional synchronization, but you can choose to enable only a one way synchronization (Jellyfin -> TubeArchivist or TubeArchivist -> Jellyfin) too.</p>

#### Jellyfin -> TubeArchivist playlists synchronization
<p>There is a task that retrieves playlists and recreates them on TubeArchivist with the videos in the same order. Please note that playlists can also have videos not beloging from TubeArchivist, they will be simply ignored, so you won't find them on TubeArchivist playlist.</p>
<p>It is present also a setting to automatically delete playlists from TubeArchivist when they are no more available on Jellyfin.</p>

#### TubeArchivist -> Jellyfin playlists synchronization
<p>There is a task that retrieves playlists from TubeArchivist and recreates them on Jellyfin with videos in the same order.</p>
<p>It is present, also in this case, a setting to automatically delete playlists from Jellyfin when they are no more present on TubeArchivist, but beware that the will be deleted also if they contain videos not beloning to TubeArchivist.</p>

> [!CAUTION]
> Pay attention when you enable the automatic deletion options, be sure that is your wanted behavior, especially when playlists contain also other videos not belonging from TubeArchivist, playlists removed won't be available again, there's no undo!


## Tasks intervals
<p>Since many of the feature are implemented as background tasks periodically executing, in the `Tasks intervals` section you will find the settings to adjust this period in seconds.<br>
Keep in mind that Jellyfin lowest accepted period is of 1 minute (60 seconds) and the lower is the interval the higher will be the resources consuption on your system.</p>
<p>Here are the configurable intervals:</p>

![Tasks intervals settings](https://github.com/user-attachments/assets/19db6b83-6715-477d-8ce7-b78526e87ba9)

## Episode numbering

<p>There are different ways to number the episodes as they are configured in Jellyfin.<br>
This changes the number after E in S--E-- (for example S2024E100 for episode number 100 of season 2024).</p>

![Episode numbering scheme options](https://github.com/user-attachments/assets/6d36bc2c-ca9d-4a5c-8021-e15d399316fc)

The options correlate with:
- Default - leave the numbering to what Jellyfin does by default (this is what the plugin has always done)
- YYYYMMDD - numbers the episode by the year, month, day (e.g. 20250804 for a video published on the 4th of August 2025)

## Build

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). This build targets Jellyfin `12.0` (plugin ABI `12.0.0.0`).

2. Run the validation harness, then publish the plugin:

   ```sh
   dotnet run --project validation/Validation.csproj --configuration Release
   dotnet publish Jellyfin.Plugin.TubeArchivistMetadata --configuration Release --output bin
   ```

3. Stop Jellyfin and place `bin/Jellyfin.Plugin.TubeArchivistMetadata.dll` in a single `plugins/TubeArchivistMetadata` folder. Follow the upgrade guidance above to remove duplicate plugin copies while retaining configuration, then start Jellyfin.

## Publish release

Publishing is automated with the GitHub Actions workflow in `.github/workflows/build.yaml`.

- Downstream builds use the fourth version component after the upstream version (example: upstream `1.5.0.0` becomes downstream `1.5.0.1`). Keep `build.yaml` and all three version fields in `Directory.Build.props` aligned.
- Create and push a version tag prefixed with `v` (example: `v1.5.0.1`) to trigger release publishing automatically.
- Or run the workflow manually from the Actions tab and provide an existing tag value.
- The workflow validates the repair, builds the tagged source, uploads the ZIP to this fork's release, and commits the generated catalog entry and checksum to the default branch. Keep historical catalog entries; add the new version only after its release artifact exists.

## License

This plugins code and packages are distributed under the GPLv3 License. See [LICENSE](./LICENSE) for more information.

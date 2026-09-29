using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Penban.Maui.Views.Widget;
using UIKit;

namespace Penban.Maui;

/// <summary>
/// The scene the app runs in. iOS 27 starts every app as a scene and refuses to start one whose
/// plist has no scene manifest; this is the class that manifest names
/// (<c>UISceneDelegateClassName</c> in Platforms/iOS/Info.plist). It does what
/// <see cref="MauiUISceneDelegate"/> does - MAUI's window is built in
/// <see cref="MauiUISceneDelegate.WillConnect"/> - and takes the link a tapped widget carries off
/// the scene's hands.
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
	/// <summary>
	/// Reads the board a tapped home screen widget asks for, <c>penban://board/&lt;id&gt;</c>, and
	/// notes it on <see cref="WidgetBoardLink"/>, from where the overview picks it up.
	/// <para>
	/// MAUI's own <c>SceneOpenUrl</c> lifecycle event does not serve here: it is dispatched through
	/// the window's handler, and a tap on the widget while the app is not running opens it from
	/// cold, where the URL is handed over right after the scene connects - before the window, and
	/// with it the page that would open the board, exists. The application's services are already
	/// up at that point, so the board is noted here directly.
	/// </para>
	/// <para>
	/// The widget's address reaches this class by two ways and both are needed: a tap on an app that
	/// is already running comes through <see cref="OpenUrl"/>, while one that starts the app from
	/// cold comes with the options the scene is connected with, in
	/// <see cref="WillConnect"/>.
	/// </para>
	/// </summary>
	public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
	{
		base.WillConnect(scene, session, connectionOptions);

		// A tap on the widget that starts the app from cold hands the address over here, among the
		// options the scene is connected with - and not to OpenUrl below, which is only told once
		// the scene is already up. The base call comes first because it is what builds MAUI's
		// window; the board is then noted under it.
		if (connectionOptions.UrlContexts is { } urlContexts)
		{
			foreach (var urlContext in urlContexts)
			{
				NoteRequestedBoard(urlContext.Url);
			}
		}
	}

	public override bool OpenUrl(UIScene scene, NSSet<UIOpenUrlContext> urlContexts)
	{
		var handled = base.OpenUrl(scene, urlContexts);

		foreach (var urlContext in urlContexts)
		{
			handled |= NoteRequestedBoard(urlContext.Url);
		}

		return handled;
	}

	/// <summary>
	/// Notes the board the widget stands for, if that is what the address asks for.
	/// </summary>
	/// <returns>Whether the address was one of the widget's.</returns>
	private static bool NoteRequestedBoard(NSUrl? url)
	{
		if (!TryReadBoardAddress(url, out var boardId))
		{
			return false;
		}

		IPlatformApplication.Current?.Services?.GetRequiredService<WidgetBoardLink>().Request(boardId);
		return true;
	}

	/// <summary>Reads the address a tapped widget carries: <c>penban://board/&lt;id&gt;</c>.</summary>
	private static bool TryReadBoardAddress(NSUrl? url, out Guid boardId)
	{
		boardId = Guid.Empty;

		const string prefix = "penban://board/";
		var address = url?.AbsoluteString;

		return address is not null
			&& address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			&& Guid.TryParse(address[prefix.Length..], out boardId);
	}
}

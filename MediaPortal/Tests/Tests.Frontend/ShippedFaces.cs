/*
 * MediaPortal 2 - Avalonia front end
 *
 * This code distributed under the GPLv3. The source of the project is
 * located at https://github.com/Ge2301/multiplatform
 */

using MediaPortal.Frontend.Theming;

namespace Tests.Frontend
{
  /// <summary>
  /// Where the two shipped faces really are, said out loud rather than inherited from whatever the test before
  /// this one left standing.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <see cref="SkinEngine"/> reads a face from the disk folder when there is one and from the assembly's own
  /// assets when there is not, and both of those places are statics a fixture can move. A test that wears Nereus
  /// or WMC therefore asks for the assembly's copy twice over - no disk folder, the assembly's asset root - and
  /// not once, because a single unanswered half is enough to make <see cref="SkinEngine.Installed"/> answer with
  /// somebody else's skins. The failure that looks like is a silent one: <see cref="SkinEngine.Apply"/> says no
  /// and gives no reason, since "no skin of that name exists" is not a complaint when nobody asked for a folder.
  /// </para>
  /// </remarks>
  internal static class ShippedFaces
  {
    /// <summary>The asset root the front end ships its faces under, spelled the way the engine spells it.</summary>
    public static string Root =>
        "avares://" + typeof(SkinEngine).Assembly.GetName().Name + "/Skins/";

    /// <summary>
    /// Ask the assembly's own copies of the shipped faces: no disk folder in front of them, and the asset root
    /// they were compiled under.
    /// </summary>
    public static void AnswerFromTheAssembly()
    {
      SkinEngine.SkinsFolder = null;
      SkinEngine.AssetsRoot = Root;
    }
  }
}

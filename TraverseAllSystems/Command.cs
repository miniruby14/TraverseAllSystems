#region Namespaces
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
#endregion

namespace TraverseAllSystems
{
  [Transaction( TransactionMode.Manual )]
  public class Command : IExternalCommand
  {
    /// <summary>
    /// Return true to include this system in the 
    /// exported system graphs.
    /// </summary>
    static bool IsDesirableSystemPredicate( MEPSystem s )
    {
      return 1 < s.Elements.Size
        && !s.Name.Equals( "unassigned" )
        && ( ( s is MechanicalSystem 
            && ( (MechanicalSystem) s ).IsWellConnected )
          || ( s is PipingSystem 
            && ( (PipingSystem) s ).IsWellConnected ) );
    }

    /// <summary>
    /// Create a and return the path of a random temporary directory.
    /// </summary>
    static string GetTemporaryDirectory()
    {
      string tempDirectory = Path.Combine(
        Path.GetTempPath(), Path.GetRandomFileName() );

      Directory.CreateDirectory( tempDirectory );

      return tempDirectory;
    }

    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;
      Application app = uiapp.Application;
      Document doc = uidoc.Document;

      FilteredElementCollector allSystems
        = new FilteredElementCollector( doc )
          .OfClass( typeof( MEPSystem ) );

      int nAllSystems = allSystems.Count<Element>();

      IEnumerable<MEPSystem> desirableSystems
        = allSystems.Cast<MEPSystem>().Where<MEPSystem>(
          s => IsDesirableSystemPredicate( s ) );

      int nDesirableSystems = desirableSystems
        .Count<Element>();

      // Check for shared parameter
      // to store graph information.

      Definition def = SharedParameterMgr.GetDefinition(
        desirableSystems.First<MEPSystem>() );

      if( null == def )
      {
        message = "Please initialise the MEP graph "
          + "storage shared parameter before "
          + "launching this command.";

        return Result.Failed;
      }

      string outputFolder = GetTemporaryDirectory();

      int nXmlFiles = 0;
      int nJsonGraphs = 0;
      int nJsonBytes = 0;

      using( Transaction t = new Transaction( doc ) )
      {
        t.Start( "Determine MEP Graph Structure and Store in JSON Shared Parameter" );

        foreach( MEPSystem system in desirableSystems )
        {
          Debug.Print( system.Name );

          // Debug test -- limit to HWS systems.
          //if( !system.Name.StartsWith( "HWS" ) ) { continue; }

          FamilyInstance root = system.BaseEquipment;

          // Traverse the system and dump the 
          // traversal graph into an XML file

          TraversalTree tree = new TraversalTree( system );

          if( tree.Traverse() )
          {
            string filename = system.Id.IntegerValue.ToString();

            filename = Path.ChangeExtension(
              Path.Combine( outputFolder, filename ), "xml" );

            tree.DumpIntoXML( filename );

            // Uncomment to preview the 
            // resulting XML structure

            //Process.Start( fileName );

            string json = tree.DumpToJson();

            Debug.Assert( 2 < json.Length, 
              "expected valid non-empty JSON graph data" );

            Debug.Print( json );

            Parameter p = system.get_Parameter( def );
            p.Set( json );

            nJsonBytes += json.Length;
            ++nJsonGraphs;
            ++nXmlFiles;
          }
        }
        t.Commit();
      }

      string main = string.Format(
        "{0} XML files and {1} JSON graphs ({2} bytes) "
        + "generated in {3} ({4} total systems, {5} desirable):",
        nXmlFiles, nJsonGraphs, nJsonBytes, 
        outputFolder, nAllSystems, nDesirableSystems );

      List<string> system_list = desirableSystems
        .Select<Element, string>( e =>
          string.Format( "{0}({1})", e.Id, e.Name ) )
        .ToList<string>();

      system_list.Sort();

      string detail = string.Join( ", ",
        system_list.ToArray<string>() );

      TaskDialog dlg = new TaskDialog( 
        nXmlFiles.ToString() + " Systems" );

      dlg.MainInstruction = main;
      dlg.MainContent = detail;

      dlg.Show();

      return Result.Succeeded;
    }
  }
}

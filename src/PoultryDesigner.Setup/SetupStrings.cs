namespace PoultryDesigner.Setup;

internal enum SetupLanguage { English, SpanishSpain, SpanishMexico }

internal sealed class SetupStrings(SetupLanguage language)
{
    internal SetupLanguage Language { get; } = language;
    internal string Code => Language switch { SetupLanguage.SpanishSpain => "es-ES", SetupLanguage.SpanishMexico => "es-MX", _ => "en" };
    internal static SetupLanguage FromCode(string code) => code switch { "es-ES" => SetupLanguage.SpanishSpain, "es-MX" => SetupLanguage.SpanishMexico, _ => SetupLanguage.English };
    internal string this[string key]
    {
        get
        {
            var text = Text[key];
            return Language switch { SetupLanguage.SpanishSpain => text.Spain, SetupLanguage.SpanishMexico => text.Mexico, _ => text.English };
        }
    }
    internal static IEnumerable<string> Keys => Text.Keys;
    private static readonly Dictionary<string, (string English, string Spain, string Mexico)> Text = new()
    {
        ["Setup"] = ("Experiment Designer Setup", "Instalación de Experiment Designer", "Instalación de Experiment Designer"),
        ["LanguageTitle"] = ("Select setup language", "Seleccione el idioma de instalación", "Seleccione el idioma de instalación"),
        ["LanguageIntro"] = ("Choose the language to use during installation.", "Elija el idioma que se utilizará durante la instalación.", "Elija el idioma que se utilizará durante la instalación."),
        ["LanguageLabel"] = ("Installation language", "Idioma de instalación", "Idioma de instalación"),
        ["Welcome"] = ("Install Experiment Designer on your computer.", "Instale Experiment Designer en su ordenador.", "Instale Experiment Designer en su equipo."),
        ["Developer"] = ("Developed by Emmanuel O. Alagbe", "Desarrollado por Emmanuel O. Alagbe", "Desarrollado por Emmanuel O. Alagbe"),
        ["Version"] = ("Version", "Versión", "Versión"),
        ["Back"] = ("< Back", "< Atrás", "< Atrás"),
        ["Next"] = ("Next >", "Siguiente >", "Siguiente >"),
        ["Cancel"] = ("Cancel", "Cancelar", "Cancelar"),
        ["Install"] = ("Install", "Instalar", "Instalar"),
        ["Retry"] = ("Retry", "Reintentar", "Reintentar"),
        ["Finish"] = ("Finish", "Finalizar", "Finalizar"),
        ["Close"] = ("Close", "Cerrar", "Cerrar"),
        ["LicenseTitle"] = ("License agreement", "Acuerdo de licencia", "Acuerdo de licencia"),
        ["LicenseIntro"] = ("Read the license before continuing.", "Lea la licencia antes de continuar.", "Lea la licencia antes de continuar."),
        ["LicenseNote"] = ("MIT License · Copyright © 2026 Emmanuel O. Alagbe", "Licencia MIT · Copyright © 2026 Emmanuel O. Alagbe", "Licencia MIT · Copyright © 2026 Emmanuel O. Alagbe"),
        ["OriginalLicense"] = ("The original MIT license is shown below.", "A continuación se muestra la licencia MIT original en inglés.", "A continuación se muestra la licencia MIT original en inglés."),
        ["AcceptLicense"] = ("I accept the agreement", "Acepto el acuerdo", "Acepto el acuerdo"),
        ["DeclineLicense"] = ("I do not accept the agreement", "No acepto el acuerdo", "No acepto el acuerdo"),
        ["LicenseMissing"] = ("The license file is missing. Extract the complete installation package and try again.", "Falta el archivo de licencia. Extraiga el paquete de instalación completo e inténtelo de nuevo.", "Falta el archivo de licencia. Extraiga el paquete de instalación completo e inténtelo de nuevo."),
        ["DestinationTitle"] = ("Select destination folder", "Seleccione la carpeta de destino", "Seleccione la carpeta de destino"),
        ["DestinationIntro"] = ("Choose where Experiment Designer will be installed.", "Elija dónde se instalará Experiment Designer.", "Elija dónde se instalará Experiment Designer."),
        ["Destination"] = ("Installation folder", "Carpeta de instalación", "Carpeta de instalación"),
        ["Browse"] = ("Browse…", "Examinar…", "Examinar…"),
        ["DestinationHelp"] = ("Setup installs for your Windows account. Choose a folder you can write to.", "La instalación se realizará para su cuenta de Windows. Elija una carpeta en la que tenga permiso de escritura.", "La instalación se realizará para su cuenta de Windows. Elija una carpeta en la que tenga permiso de escritura."),
        ["Existing"] = ("An existing installation was found. Setup will update it.", "Se ha encontrado una instalación existente. Se actualizará.", "Se encontró una instalación existente. Se actualizará."),
        ["FolderRequired"] = ("Enter an absolute installation folder, such as C:\\Users\\YourName\\AppData\\Local\\ExperimentDesigner.", "Introduzca la ruta absoluta de la carpeta de instalación.", "Escriba la ruta absoluta de la carpeta de instalación."),
        ["FolderInvalid"] = ("This installation folder cannot be used. Choose another folder.", "No se puede utilizar esta carpeta de instalación. Elija otra carpeta.", "No se puede usar esta carpeta de instalación. Elija otra carpeta."),
        ["MenuTitle"] = ("Select Start Menu folder", "Seleccione la carpeta del menú Inicio", "Seleccione la carpeta del menú Inicio"),
        ["MenuIntro"] = ("Choose where to place the program shortcut.", "Elija dónde se creará el acceso directo del programa.", "Elija dónde se creará el acceso directo del programa."),
        ["MenuLabel"] = ("Start Menu folder", "Carpeta del menú Inicio", "Carpeta del menú Inicio"),
        ["CreateMenu"] = ("Create a Start Menu shortcut", "Crear un acceso directo en el menú Inicio", "Crear un acceso directo en el menú Inicio"),
        ["MenuHelp"] = ("The shortcut will appear in the Start Menu for your Windows account.", "El acceso directo aparecerá en el menú Inicio de su cuenta de Windows.", "El acceso directo aparecerá en el menú Inicio de su cuenta de Windows."),
        ["MenuInvalid"] = ("Enter a valid folder name inside the Start Menu.", "Introduzca un nombre de carpeta válido dentro del menú Inicio.", "Escriba un nombre de carpeta válido dentro del menú Inicio."),
        ["TasksTitle"] = ("Select additional tasks", "Seleccione tareas adicionales", "Seleccione tareas adicionales"),
        ["TasksIntro"] = ("Choose the shortcuts you would like Setup to create.", "Elija los accesos directos que desea crear.", "Elija los accesos directos que desea crear."),
        ["Desktop"] = ("Create a desktop shortcut", "Crear un acceso directo en el escritorio", "Crear un acceso directo en el escritorio"),
        ["TasksHelp"] = ("Windows Settings will include an uninstall entry for Experiment Designer.", "Experiment Designer aparecerá en la lista de aplicaciones de Configuración de Windows.", "Experiment Designer aparecerá en la lista de aplicaciones de Configuración de Windows."),
        ["ReadyTitle"] = ("Ready to install", "Preparado para instalar", "Listo para instalar"),
        ["ReadyIntro"] = ("Review your choices, then select Install to continue.", "Revise las opciones y pulse Instalar para continuar.", "Revise las opciones y seleccione Instalar para continuar."),
        ["Yes"] = ("Yes", "Sí", "Sí"),
        ["No"] = ("No", "No", "No"),
        ["None"] = ("None", "Ninguna", "Ninguna"),
        ["InstallingTitle"] = ("Installing", "Instalando", "Instalando"),
        ["InstallingIntro"] = ("Please wait while Setup installs Experiment Designer.", "Espere mientras se instala Experiment Designer en su ordenador.", "Espere mientras se instala Experiment Designer en su equipo."),
        ["Verifying"] = ("Verifying installation files…", "Verificando los archivos de instalación…", "Verificando los archivos de instalación…"),
        ["Copying"] = ("Installing application files…", "Instalando los archivos de la aplicación…", "Instalando los archivos de la aplicación…"),
        ["Registering"] = ("Creating shortcuts and Windows uninstall entry…", "Creando los accesos directos y la entrada de desinstalación de Windows…", "Creando los accesos directos y la entrada de desinstalación de Windows…"),
        ["CompleteTitle"] = ("Completing the setup wizard", "Finalizando el asistente de instalación", "Finalizando el asistente de instalación"),
        ["CompleteIntro"] = ("Experiment Designer has been installed successfully.", "Experiment Designer se ha instalado correctamente.", "Experiment Designer se instaló correctamente."),
        ["CompleteHelp"] = ("Select Finish to close Setup.", "Pulse Finalizar para cerrar el asistente.", "Seleccione Finalizar para cerrar el asistente."),
        ["Launch"] = ("Launch Experiment Designer", "Iniciar Experiment Designer", "Iniciar Experiment Designer"),
        ["Cancelled"] = ("Installation cancelled.", "Instalación cancelada.", "Instalación cancelada."),
        ["CancelQuestion"] = ("Exit Setup? Installation has not finished.", "¿Desea salir del asistente? La instalación no ha finalizado.", "¿Desea salir del asistente? La instalación no ha terminado."),
        ["CancelInstall"] = ("Cancel installation? Please wait while Setup rolls back incomplete files.", "¿Desea cancelar la instalación? Espere mientras se retiran los archivos incompletos.", "¿Desea cancelar la instalación? Espere mientras se retiran los archivos incompletos."),
        ["Cancelling"] = ("Cancelling installation…", "Cancelando la instalación…", "Cancelando la instalación…"),
        ["InstallFailed"] = ("Setup could not complete the installation. You can retry.", "No se ha podido completar la instalación. Puede reintentarlo.", "No se pudo completar la instalación. Puede reintentarlo."),
        ["RemoveTitle"] = ("Remove Experiment Designer", "Desinstalar Experiment Designer", "Desinstalar Experiment Designer"),
        ["RemoveIntro"] = ("Remove this installation from your Windows account.", "Se desinstalará Experiment Designer de su cuenta de Windows.", "Se desinstalará Experiment Designer de su cuenta de Windows."),
        ["RemoveHelp"] = ("Your saved studies and any unknown or modified files will be preserved.", "Se conservarán sus estudios guardados y los archivos desconocidos o modificados.", "Se conservarán sus estudios guardados y los archivos desconocidos o modificados."),
        ["Remove"] = ("Uninstall", "Desinstalar", "Desinstalar"),
        ["RemoveQuestion"] = ("Uninstall Experiment Designer from this folder?", "¿Desea desinstalar Experiment Designer de esta carpeta?", "¿Desea desinstalar Experiment Designer de esta carpeta?"),
        ["Removing"] = ("Removing installed files…", "Desinstalando los archivos…", "Desinstalando los archivos…"),
        ["Removed"] = ("Experiment Designer has been removed.", "Experiment Designer se ha desinstalado.", "Experiment Designer se desinstaló."),
        ["RemoveFailed"] = ("Setup could not complete removal. Close the app and retry.", "No se ha podido completar la desinstalación. Cierre la aplicación y reinténtelo.", "No se pudo completar la desinstalación. Cierre la aplicación y reinténtelo."),
        ["OpenFailed"] = ("Experiment Designer could not be opened.", "No se ha podido abrir Experiment Designer.", "No se pudo abrir Experiment Designer."),
        ["Details"] = ("Details", "Detalles", "Detalles")
    };
}

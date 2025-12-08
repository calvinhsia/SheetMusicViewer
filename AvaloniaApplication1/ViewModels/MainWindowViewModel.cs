using AvaloniaApplication1.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AvaloniaApplication1.ViewModels
{
    public class MainWindowViewModel
    {
        public ObservableCollection<Person> People { get; }

        public MainWindowViewModel()
        {
            var people = new List<Person>
            {
                new Person("Neil", "Armstrong", false),
                new Person("Buzz", "Lightyear", true),
                new Person("James", "Kirk", true)
            };
            People = new ObservableCollection<Person>(people);
        }
    }
}

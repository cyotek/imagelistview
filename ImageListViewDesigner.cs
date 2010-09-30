﻿// ImageListView - A listview control for image files
// Copyright (C) 2009 Ozgur Ozcitak
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Ozgur Ozcitak (ozcitak@yahoo.com)

using System;
using System.Windows.Forms;
using System.ComponentModel;
using System.Drawing.Design;
using System.ComponentModel.Design;
using System.Windows.Forms.Design;
using System.Drawing;
using System.Windows.Forms.Design.Behavior;

namespace Manina.Windows.Forms
{
    /// <summary>
    /// Represents the designer of the image list view.
    /// </summary>
    internal class ImageListViewDesigner : ControlDesigner
    {
        #region Member Variables
        private DesignerActionListCollection actionLists = null;
        private ImageListView imageListView;
        private ImageListViewItem[] items;
        #endregion

        #region Add/Remove Glyphs on Initialize/Dispose
        /// <summary>
        /// Initializes the designer with the specified component.
        /// </summary>
        /// <param name="component">The <see cref="T:System.ComponentModel.IComponent"/> 
        /// to associate the designer with. This component must always be an instance of, 
        /// or derive from, <see cref="T:System.Windows.Forms.Control"/>.</param>
        public override void Initialize(IComponent component)
        {
            base.Initialize(component);

            imageListView = (ImageListView)this.Control;

            // Add preview items
            items = new ImageListViewItem[3];
            for (int i = 0; i < items.Length; i++)
            {
                ImageListViewItem item = new ImageListViewItem();
                item.Text = "Item " + (i + 1).ToString();
                item.mImageListView = imageListView;
                item.Tag = null;
                items[i] = item;
            }
        }
        #endregion

        #region Designer Action Lists
        /// <summary>
        /// Gets the design-time action lists supported by the component associated with the designer.
        /// </summary>
        public override DesignerActionListCollection ActionLists
        {
            get
            {
                if (null == actionLists)
                {
                    actionLists = base.ActionLists;
                    actionLists.Add(new ImageListViewActionLists(this.Component));
                }
                return actionLists;
            }
        }
        #endregion

        #region Paint Adornments
        /// <summary>
        /// Receives a call when the control that the designer is managing has painted 
        /// its surface so the designer can paint any additional adornments on top of the control.
        /// </summary>
        /// <param name="pe">A <see cref="T:System.Windows.Forms.PaintEventArgs"/> the designer 
        /// can use to draw on the control.</param>
        protected override void OnPaintAdornments(PaintEventArgs pe)
        {
            base.OnPaintAdornments(pe);

            if (imageListView.Items.Count == 0)
            {
                imageListView.layoutManager.Update(true);

                for (int i = 0; i < items.Length; i++)
                {
                    ImageListViewItem item = items[i];
                    Rectangle bounds = imageListView.layoutManager.GetItemBounds(i);

                    // Add custom columns
                    if (item.Tag == null)
                    {
                        int c = 0;
                        foreach (ImageListView.ImageListViewColumnHeader column in imageListView.Columns)
                        {
                            if (column.Type == ColumnType.Custom)
                            {
                                item.AddSubItemText(column.columnID);
                                c++;
                            }
                        }
                        item.Tag = c.ToString();
                    }

                    Rectangle itemArea = imageListView.layoutManager.ItemAreaBounds;
                    Rectangle clip = Rectangle.Intersect(Rectangle.Intersect(bounds, itemArea), pe.ClipRectangle);
                    //pe.Graphics.SetClip(clip);
                    imageListView.mRenderer.DrawItem(pe.Graphics, item, ItemState.None, bounds);

                    if (imageListView.ShowCheckBoxes)
                    {
                        Rectangle wbounds = imageListView.layoutManager.GetCheckBoxBounds(i);
                        imageListView.mRenderer.DrawCheckBox(pe.Graphics, item, wbounds);
                    }
                    if (imageListView.ShowFileIcons)
                    {
                        Rectangle wbounds = imageListView.layoutManager.GetIconBounds(i);
                        imageListView.mRenderer.DrawFileIcon(pe.Graphics, item, wbounds);
                    }
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// Defines smart tag entries for the image list view.
    /// </summary>
    internal class ImageListViewActionLists : DesignerActionList, IServiceProvider, IWindowsFormsEditorService, ITypeDescriptorContext
    {
        #region Member Variables
        private ImageListView imageListView;
        private DesignerActionUIService designerService;

        private PropertyDescriptor columnProperty;
        private PropertyDescriptor itemProperty;
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the ImageListViewActionLists class.
        /// </summary>
        /// <param name="component">A component related to the DesignerActionList.</param>
        public ImageListViewActionLists(IComponent component)
            : base(component)
        {
            imageListView = (ImageListView)component;

            designerService = (DesignerActionUIService)GetService(typeof(DesignerActionUIService));
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Helper method to retrieve control properties for undo support.
        /// </summary>
        /// <param name="propName">Property name.</param>
        private PropertyDescriptor GetPropertyByName(String propName)
        {
            PropertyDescriptor prop;
            prop = TypeDescriptor.GetProperties(imageListView)[propName];
            if (prop == null)
                throw new ArgumentException("Unknown property.", propName);
            else
                return prop;
        }
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the sort column of the designed ImageListView.
        /// </summary>
        public int SortColumn
        {
            get { return imageListView.SortColumn; }
            set { GetPropertyByName("SortColumn").SetValue(imageListView, value); }
        }
        /// <summary>
        /// Gets or sets the sort oerder of the designed ImageListView.
        /// </summary>
        public SortOrder SortOrder
        {
            get { return imageListView.SortOrder; }
            set { GetPropertyByName("SortOrder").SetValue(imageListView, value); }
        }
        /// <summary>
        /// Gets or sets the view mode of the designed ImageListView.
        /// </summary>
        public View View
        {
            get { return imageListView.View; }
            set { GetPropertyByName("View").SetValue(imageListView, value); }
        }
        #endregion

        #region Instance Methods
        /// <summary>
        /// Invokes the editor for the columns of the designed ImageListView.
        /// </summary>
        public void EditColumns()
        {
            // IComponentChangeService is used to pass change notifications to the designer
            IComponentChangeService ccs = (IComponentChangeService)GetService(typeof(IComponentChangeService));

            // Get the collection editor
            columnProperty = GetPropertyByName("Columns");
            UITypeEditor editor = (UITypeEditor)columnProperty.GetEditor(typeof(UITypeEditor));
            object value = imageListView.Columns;

            // Notify the designers of the change
            if (ccs != null)
                ccs.OnComponentChanging(imageListView, columnProperty);

            // Edit the value
            value = editor.EditValue(this, this, value);
            imageListView.Columns = (ImageListView.ImageListViewColumnHeaderCollection)value;

            // Notify the designers of the change
            if (ccs != null)
                ccs.OnComponentChanged(imageListView, columnProperty, null, null);

            designerService.Refresh(Component);
        }
        /// <summary>
        /// Invokes the editor for the items of the designed ImageListView.
        /// </summary>
        public void EditItems()
        {
            // IComponentChangeService is used to pass change notifications to the designer
            IComponentChangeService ccs = (IComponentChangeService)GetService(typeof(IComponentChangeService));

            // Get the collection editor
            itemProperty = GetPropertyByName("Items");
            UITypeEditor editor = (UITypeEditor)itemProperty.GetEditor(typeof(UITypeEditor));
            object value = imageListView.Items;

            // Notify the designers of the change
            if (ccs != null)
                ccs.OnComponentChanging(imageListView, itemProperty);

            // Edit the value
            value = editor.EditValue(this, this, value);
            imageListView.Items = (ImageListView.ImageListViewItemCollection)value;

            // Notify the designers of the change
            if (ccs != null)
                ccs.OnComponentChanged(imageListView, itemProperty, null, null);

            designerService.Refresh(Component);
        }
        #endregion

        #region DesignerActionList Overrides
        /// <summary>
        /// Returns the collection of <see cref="T:System.ComponentModel.Design.DesignerActionItem"/> objects contained in the list.
        /// </summary>
        public override DesignerActionItemCollection GetSortedActionItems()
        {
            DesignerActionItemCollection items = new DesignerActionItemCollection();

            items.Add(new DesignerActionMethodItem(this, "EditItems", "Edit Items", true));
            items.Add(new DesignerActionMethodItem(this, "EditColumns", "Edit Columns", true));

            items.Add(new DesignerActionPropertyItem("View", "View"));
            items.Add(new DesignerActionPropertyItem("SortColumn", "SortColumn"));
            items.Add(new DesignerActionPropertyItem("SortOrder", "SortOrder"));

            return items;
        }
        #endregion

        #region IServiceProvider Members
        /// <summary>
        /// Returns an object that represents a service provided by the component 
        /// associated with the <see cref="T:System.ComponentModel.Design.DesignerActionList"/>.
        /// </summary>
        object IServiceProvider.GetService(Type serviceType)
        {
            if (serviceType.Equals(typeof(IWindowsFormsEditorService)))
            {
                return this;
            }
            return GetService(serviceType);
        }
        #endregion

        #region IWindowsFormsEditorService Members
        /// <summary>
        /// Closes any previously opened drop down control area.
        /// </summary>
        void IWindowsFormsEditorService.CloseDropDown()
        {
            throw new NotSupportedException("Only modal dialogs are supported.");
        }
        /// <summary>
        /// Displays the specified control in a drop down area below a value 
        /// field of the property grid that provides this service.
        /// </summary>
        void IWindowsFormsEditorService.DropDownControl(Control control)
        {
            throw new NotSupportedException("Only modal dialogs are supported.");
        }
        /// <summary>
        /// Shows the specified <see cref="T:System.Windows.Forms.Form"/>.
        /// </summary>
        DialogResult IWindowsFormsEditorService.ShowDialog(Form dialog)
        {
            return (dialog.ShowDialog());
        }
        #endregion

        #region ITypeDescriptorContext Members
        /// <summary>
        /// Gets the container representing this 
        /// <see cref="T:System.ComponentModel.TypeDescriptor"/> request.
        /// </summary>
        IContainer ITypeDescriptorContext.Container
        {
            get { return null; }
        }
        /// <summary>
        /// Gets the object that is connected with this type descriptor request.
        /// </summary>
        object ITypeDescriptorContext.Instance
        {
            get { return imageListView; }
        }
        /// <summary>
        /// Raises the <see cref="E:System.ComponentModel.Design.IComponentChangeService.ComponentChanged"/> event.
        /// </summary>
        void ITypeDescriptorContext.OnComponentChanged()
        {
            ;
        }
        /// <summary>
        /// Raises the <see cref="E:System.ComponentModel.Design.IComponentChangeService.ComponentChanging"/> event.
        /// </summary>
        bool ITypeDescriptorContext.OnComponentChanging()
        {
            return true;
        }
        /// <summary>
        /// Gets the <see cref="T:System.ComponentModel.PropertyDescriptor"/> 
        /// that is associated with the given context item.
        /// </summary>
        PropertyDescriptor ITypeDescriptorContext.PropertyDescriptor
        {
            get { return columnProperty; }
        }
        #endregion
    }
}
